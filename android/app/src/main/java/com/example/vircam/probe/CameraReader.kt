package com.example.vircam.probe

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.ImageFormat
import android.hardware.camera2.CameraCaptureSession
import android.hardware.camera2.CameraDevice
import android.hardware.camera2.CameraManager
import android.hardware.camera2.CaptureRequest
import android.media.ImageReader
import android.os.Handler
import android.os.HandlerThread
import android.os.SystemClock
import android.util.Log
import android.util.Range
import com.example.vircam.core.FrameEvidence
import com.example.vircam.core.FrameMarker
import com.example.vircam.core.ProbeGuard
import org.json.JSONObject

data class ProbeFrame(val marker: Int, val accepted: Int, val timestamp: Long, val preview: Bitmap?)

/** Owns one foreground camera session. Does not select or fall back to another camera. */
class CameraReader(
    private val context: Context,
    private val displayId: Int,
    private val expectedDevice: Int,
    private val cameraId: String,
    private val sessionId: String,
    private val onFrame: (ProbeFrame) -> Unit,
    private val onError: (String) -> Unit
) : AutoCloseable {
    private val thread = HandlerThread("vircre-camera-reader")
    private val main = Handler(context.mainLooper)
    private var worker: Handler? = null
    private var camera: CameraDevice? = null
    private var capture: CameraCaptureSession? = null
    private var images: ImageReader? = null
    @Volatile private var closed = false
    private val evidence = FrameEvidence()
    private var lastTimestamp = -1L
    @Volatile private var lastFresh = SystemClock.elapsedRealtime()
    private val staleCheck = object : Runnable {
        override fun run() {
            if (closed) return
            if (SystemClock.elapsedRealtime() - lastFresh > 3000) {
                fail("3 秒未读到新的有效标记，已停止并清空画面")
            } else main.postDelayed(this, 500)
        }
    }

    fun start() {
        check(worker == null) { "CameraReader is single-use" }
        val manager = context.getSystemService(CameraManager::class.java)
        val available = manager.cameraIdList
        ProbeLog.write(context, sessionId, JSONObject().put("event", "camera-list")
            .put("expected", cameraId).put("available", org.json.JSONArray(available.toList()))
            .put("deviceId", context.deviceId))
        val problem = ProbeGuard.problem(displayId, context.deviceId, expectedDevice, cameraId, available)
        require(problem == null) { problem ?: "Invalid camera context" }
        check(context.checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
            "请先通过系统提示授权摄像头"
        }
        thread.start()
        val handler = Handler(thread.looper)
        worker = handler
        val reader = ImageReader.newInstance(640, 480, ImageFormat.YUV_420_888, 2)
        images = reader
        reader.setOnImageAvailableListener({ source ->
            if (!closed) source.acquireLatestImage()?.use { image ->
                if (closed) return@use
                val plane = image.planes[0]
                val marker = FrameMarker.sample(plane.buffer, image.width, image.height, plane.rowStride, plane.pixelStride)
                val fresh = image.timestamp > lastTimestamp && evidence.accept(marker)
                if (!fresh) return@use
                lastTimestamp = image.timestamp
                lastFresh = SystemClock.elapsedRealtime()
                val values = IntArray(image.width * image.height)
                val bytes = plane.buffer
                val base = bytes.position()
                for (y in 0 until image.height) for (x in 0 until image.width) {
                    val luma = bytes.get(base + y * plane.rowStride + x * plane.pixelStride).toInt() and 255
                    values[y * image.width + x] = (0xff shl 24) or (luma shl 16) or (luma shl 8) or luma
                }
                val preview = Bitmap.createBitmap(values, image.width, image.height, Bitmap.Config.ARGB_8888)
                val frame = ProbeFrame(marker, evidence.accepted(), image.timestamp, preview)
                ProbeLog.write(context, sessionId, JSONObject().put("event", "frame-read")
                    .put("marker", marker).put("accepted", evidence.accepted()).put("timestamp", image.timestamp)
                    .put("deviceId", context.deviceId).put("displayId", displayId).put("cameraId", cameraId))
                main.post { if (!closed) onFrame(frame) }
            }
        }, handler)
        main.postDelayed(staleCheck, 500)
        manager.openCamera(cameraId, object : CameraDevice.StateCallback() {
            override fun onOpened(device: CameraDevice) {
                if (closed) { device.close(); return }
                camera = device
                @Suppress("DEPRECATION")
                device.createCaptureSession(listOf(reader.surface), object : CameraCaptureSession.StateCallback() {
                    override fun onConfigured(session: CameraCaptureSession) {
                        if (closed) { session.close(); return }
                        capture = session
                        try {
                            val request = device.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW).apply {
                                addTarget(reader.surface)
                                set(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, Range(5, 5))
                            }.build()
                            session.setRepeatingRequest(request, null, handler)
                        } catch (e: Exception) { fail("捕获请求失败：${e.message}") }
                    }
                    override fun onConfigureFailed(session: CameraCaptureSession) { fail("相机会话配置失败") }
                }, handler)
            }
            override fun onDisconnected(device: CameraDevice) { device.close(); fail("虚拟相机已断开") }
            override fun onError(device: CameraDevice, error: Int) { device.close(); fail("相机错误：$error") }
        }, handler)
    }

    private fun fail(message: String) {
        if (closed) return
        Log.e("VirCreProbe", message)
        ProbeLog.write(context, sessionId, JSONObject().put("event", "probe-failed").put("error", message))
        close()
        main.post { onError(message) }
    }

    override fun close() {
        if (closed) return
        closed = true
        main.removeCallbacks(staleCheck)
        worker?.post {
            try { capture?.close() } finally {
                try { camera?.close() } finally {
                    images?.close()
                    thread.quitSafely()
                }
            }
        }
    }
}

package com.example.vircam

import android.Manifest
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.os.Bundle
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.unit.dp
import com.example.vircam.probe.CameraReader
import com.example.vircam.probe.ProbeLog
import com.example.vircam.ui.theme.VirCamTheme

class MainActivity : ComponentActivity() {
    private var message by mutableStateOf("等待 P0 shell 会话")
    private var preview by mutableStateOf<Bitmap?>(null)
    private var reader: CameraReader? = null
    private var attemptedLaunch = false
    private val permission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) {
            if (intent.hasExtra("virtualDeviceId")) startProbe()
            else message = "摄像头已授权；等待电脑启动虚拟显示。当前不会打开物理相机。"
        } else { message = "摄像头权限被拒绝；实验已停止"; stopProbe() }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            VirCamTheme {
                Scaffold(modifier = Modifier.fillMaxSize()) { innerPadding ->
                    Column(Modifier.fillMaxSize().padding(innerPadding).padding(20.dp)) {
                        Text("VirCam · 虚拟相机探针")
                        Spacer(Modifier.height(12.dp))
                        Text("最低 Android 16 / API 36")
                        Text("displayId=${display?.displayId} · deviceId=$deviceId")
                        Text(message)
                        Spacer(Modifier.height(12.dp))
                        preview?.let { image ->
                            Image(image.asImageBitmap(), "从 Camera2 Y 平面读取的标记画面（灰度）",
                                Modifier.fillMaxWidth().aspectRatio(4f / 3f).weight(1f, false))
                        }
                        Row {
                            OutlinedButton(onClick = { permission.launch(Manifest.permission.CAMERA) }) { Text("授权摄像头") }
                            Button(onClick = { requestProbe() }) { Text("开始读取") }
                            OutlinedButton(onClick = { stopProbe(); message = "已停止，画面已清空" }) { Text("停止") }
                        }
                        Text("从电脑运行 tools/Start-P0.ps1。默认显示中禁止打开相机。")
                    }
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        ProbeLog.write(this, intent.getStringExtra("session") ?: "permission-setup",
            org.json.JSONObject().put("event", "activity-context")
            .put("displayId", display?.displayId ?: -1).put("deviceId", deviceId)
            .put("permission", checkSelfPermission(Manifest.permission.CAMERA)))
        if (intent.hasExtra("virtualDeviceId") && !attemptedLaunch) {
            attemptedLaunch = true
            requestProbe()
        }
        else if (intent.getBooleanExtra("preparePermission", false)) {
            intent.removeExtra("preparePermission")
            permission.launch(Manifest.permission.CAMERA)
        }
    }

    override fun onPause() {
        stopProbe()
        super.onPause()
    }

    private fun requestProbe() {
        if (display?.displayId == 0 || deviceId == 0 || !intent.hasExtra("cameraId")) {
            message = "请通过电脑工具在虚拟显示中启动；当前不会打开物理相机"
            return
        }
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            permission.launch(Manifest.permission.CAMERA)
        } else startProbe()
    }

    private fun startProbe() {
        if (reader != null) return
        message = "正在连接虚拟相机，等待变化标记"
        val next = CameraReader(this, display?.displayId ?: 0,
            intent.getIntExtra("virtualDeviceId", 0), intent.getStringExtra("cameraId") ?: "",
            intent.getStringExtra("session") ?: "",intent.getStringExtra("inputMode")=="desktop",
            onFrame = { frame ->
                preview = frame.preview
                message = if(intent.getStringExtra("inputMode")=="desktop")
                    "电脑输入 · 已读取 ${frame.accepted} 帧 · ${if(frame.qrSha256!=null) "已识别二维码（不显示正文）" else "未检测到二维码"}"
                    else "已读到 ${frame.accepted} 个变化帧 · 标记 ${frame.marker}\n时间戳 ${frame.timestamp} ns"
            }, onError = { error -> reader = null; preview = null; message = error })
        reader = next
        try { next.start() } catch (e: Exception) {
            stopProbe()
            message = "启动失败：${e.message}"
            ProbeLog.write(this, intent.getStringExtra("session") ?: "", org.json.JSONObject()
                .put("event", "probe-start-failed").put("error", e.toString()))
        }
    }

    private fun stopProbe() {
        reader?.close()
        reader = null
        preview = null
    }
}

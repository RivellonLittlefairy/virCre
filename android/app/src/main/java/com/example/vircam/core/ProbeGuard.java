package com.example.vircam.core;

import java.util.Arrays;

public final class ProbeGuard {
    private ProbeGuard() {}

    public static String problem(int displayId, int deviceId, int expectedDeviceId,
            String cameraId, String[] available) {
        if (displayId <= 0 || deviceId <= 0) return "必须从虚拟设备显示启动，禁止打开物理相机";
        if (deviceId != expectedDeviceId) return "虚拟设备 ID 与启动会话不一致";
        if (cameraId == null || available == null || !Arrays.asList(available).contains(cameraId)) {
            return "当前虚拟设备未枚举到指定相机";
        }
        return null;
    }
}

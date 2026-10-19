package com.genymobile.scrcpy.control;

import java.io.BufferedReader;
import java.io.File;
import java.io.InputStreamReader;
import java.io.IOException;
import java.nio.file.Files;
import java.util.concurrent.TimeUnit;

public final class ImeLockChecks {
    public static void main(String[] args) throws Exception {
        if (args.length == 2 && args[0].equals("worker")) {
            ImeCommitter.run(new File(args[1]), 2000, () -> {
                System.out.println("LOCKED"); System.out.flush();
                File release = new File(args[1] + ".release");
                long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(10);
                try {
                    while (!release.exists() && System.nanoTime() < deadline) {Thread.sleep(5);}
                } catch (InterruptedException e) {Thread.currentThread().interrupt(); throw new IOException("interrupted");}
            });
            return;
        }
        File directory = Files.createTempDirectory(new File(args[0]).toPath(), "ime-lock-").toFile();
        File lock = new File(directory, "transaction.lock");
        String java = new File(System.getProperty("java.home"), "bin/java.exe").toString();
        Process worker = new ProcessBuilder(java, "-cp", System.getProperty("java.class.path"),
                ImeLockChecks.class.getName(), "worker", lock.toString()).redirectError(ProcessBuilder.Redirect.INHERIT).start();
        try {
            if (!"LOCKED".equals(new BufferedReader(new InputStreamReader(worker.getInputStream())).readLine())) {
                throw new AssertionError("Lock worker did not start");
            }
            boolean[] ran = {false};
            try {
                ImeCommitter.run(lock, 80, () -> ran[0] = true);
                throw new AssertionError("A second server interleaved its clipboard transaction");
            } catch (IOException expected) { }
            if (ran[0]) {throw new AssertionError("Timed-out commit changed the clipboard");}
            Files.createFile(new File(lock.toString() + ".release").toPath());
            if (!worker.waitFor(5, TimeUnit.SECONDS) || worker.exitValue() != 0) {throw new AssertionError("Lock worker did not finish");}
            ImeCommitter.run(lock, 1000, () -> ran[0] = true);
            if (!ran[0]) {throw new AssertionError("Released lock could not be reacquired");}
            try {ImeCommitter.run(lock, 1000, () -> {throw new IOException("expected action failure");});}
            catch (IOException expected) { }
            ImeCommitter.run(lock, 1000, () -> { });
            System.out.println("PASS: real cross-process lock prevents interleaving, times out without action and releases after failure");
        } finally {
            if (worker.isAlive()) {worker.destroyForcibly(); worker.waitFor();}
            Files.deleteIfExists(new File(lock.toString() + ".release").toPath());
            Files.deleteIfExists(lock.toPath()); Files.deleteIfExists(directory.toPath());
        }
    }
}

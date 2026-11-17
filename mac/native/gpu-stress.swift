// gpu-stress: Metal load for Dump Finder's stability test (Windows: Direct3D 11 compute in src/Core/Stress/GpuStress.cs).
// Usage: gpu-stress <seconds>
// Runs the same deterministic compute kernel again and again over a large buffer and compares every pass with the
// first one: a GPU that computes a different result for the same input is unstable (heat, power, faulty silicon).
// Prints one JSON line per second on stdout: {"t":…,"passes":…,"errors":…,"gflops":…}, then {"done":true,…}.
// Errors go to stderr with exit code 2 (no Metal device) or 3 (kernel did not compile).
import Foundation
import Metal

let seconds = Double(CommandLine.arguments.dropFirst().first ?? "30") ?? 30
guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else {
    FileHandle.standardError.write("no Metal device\n".data(using: .utf8)!)
    exit(2)
}

// Each thread runs a long chain of fused multiply-adds seeded by its index: heavy on the ALUs, deterministic.
let source = """
#include <metal_stdlib>
using namespace metal;
kernel void burn(device float4 *out [[buffer(0)]], constant uint &rounds [[buffer(1)]], uint id [[thread_position_in_grid]]) {
    float4 a = float4(id % 977, id % 613, id % 389, id % 211) * 0.0001f + 0.5f;
    float4 b = float4(0.9999f, 1.0001f, 0.99995f, 1.00005f);
    float4 c = float4(0.0001f, -0.0001f, 0.00005f, -0.00005f);
    for (uint i = 0; i < rounds; i++) {
        a = fma(a, b, c);
        a = fma(a, b.yxwz, -c);
        a = fma(a, b.zwxy, c.wzyx);
        a = fma(a, b.wzyx, -c.yxwz);
    }
    out[id] = a;
}
"""
let library: MTLLibrary
do { library = try device.makeLibrary(source: source, options: nil) } catch {
    FileHandle.standardError.write("kernel: \(error)\n".data(using: .utf8)!)
    exit(3)
}
let pipeline = try! device.makeComputePipelineState(function: library.makeFunction(name: "burn")!)

let count = 1 << 22                       // 4 M threads × 16 bytes = 64 MB per buffer
var rounds: UInt32 = 2048                 // 4 FMA × 4 lanes × 2 flops per round
let flopsPerPass = Double(count) * Double(rounds) * 4 * 4 * 2
let length = count * MemoryLayout<SIMD4<Float>>.stride
let reference = device.makeBuffer(length: length, options: .storageModeShared)!
let work = device.makeBuffer(length: length, options: .storageModeShared)!

func run(into buffer: MTLBuffer) {
    let cmd = queue.makeCommandBuffer()!
    let enc = cmd.makeComputeCommandEncoder()!
    enc.setComputePipelineState(pipeline)
    enc.setBuffer(buffer, offset: 0, index: 0)
    enc.setBytes(&rounds, length: 4, index: 1)
    let width = min(pipeline.maxTotalThreadsPerThreadgroup, 256)
    enc.dispatchThreads(MTLSize(width: count, height: 1, depth: 1), threadsPerThreadgroup: MTLSize(width: width, height: 1, depth: 1))
    enc.endEncoding()
    cmd.commit()
    cmd.waitUntilCompleted()
}

func emit(_ dict: [String: Any]) {
    let data = try! JSONSerialization.data(withJSONObject: dict, options: [.sortedKeys])
    FileHandle.standardOutput.write(data)
    FileHandle.standardOutput.write("\n".data(using: .utf8)!)
}

emit(["device": device.name])
let start = Date()
run(into: reference)
var passes = 0, errors = 0, lastReport = start, flopsSince = 0.0
let ref = reference.contents().bindMemory(to: SIMD4<Float>.self, capacity: count)
let cur = work.contents().bindMemory(to: SIMD4<Float>.self, capacity: count)
while Date().timeIntervalSince(start) < seconds {
    run(into: work)
    passes += 1
    flopsSince += flopsPerPass
    for i in 0..<count where cur[i] != ref[i] { errors += 1 }
    let now = Date()
    if now.timeIntervalSince(lastReport) >= 1 {
        emit(["t": now.timeIntervalSince(start), "passes": passes, "errors": errors, "gflops": flopsSince / now.timeIntervalSince(lastReport) / 1e9])
        lastReport = now
        flopsSince = 0
    }
}
emit(["done": true, "t": Date().timeIntervalSince(start), "passes": passes, "errors": errors])

// Times one 4K float4 blur-style kernel (17 reads along a column, one write) with its input and
// output buffers in MTLStorageModeShared or MTLStorageModePrivate, in all four combinations, using
// the command buffer's GPU timestamps (best of 30 per round, 3 rounds).
//
//     swift tools/storage_mode_probe.swift
//
// This is the measurement behind "GPU-private storage" in docs/unified-memory.md. It is a one-off
// probe, not part of the benchmark suite: SharpCompute only creates shared buffers.
import Foundation
import Metal

let device = MTLCreateSystemDefaultDevice()!
let queue = device.makeCommandQueue()!
let source = """
#include <metal_stdlib>
using namespace metal;
kernel void blur(const device float4* a [[buffer(0)]], device float4* b [[buffer(1)]], uint i [[thread_position_in_grid]]) {
    float4 s = 0;
    for (int k = -8; k <= 8; k++) { s += a[clamp(int(i) + k * 3840, 0, 3840 * 2160 - 1)]; }
    b[i] = s / 17;
}
"""
let library = try! device.makeLibrary(source: source, options: nil)
let pipeline = try! device.makeComputePipelineState(function: library.makeFunction(name: "blur")!)
let count = 3840 * 2160

func best(_ input: MTLBuffer, _ output: MTLBuffer) -> Double {
    var best = Double.infinity

    for _ in 0..<30 {
        let commands = queue.makeCommandBuffer()!
        let encoder = commands.makeComputeCommandEncoder()!
        encoder.setComputePipelineState(pipeline)
        encoder.setBuffer(input, offset: 0, index: 0)
        encoder.setBuffer(output, offset: 0, index: 1)
        encoder.dispatchThreads(MTLSize(width: count, height: 1, depth: 1), threadsPerThreadgroup: MTLSize(width: 256, height: 1, depth: 1))
        encoder.endEncoding()
        commands.commit()
        commands.waitUntilCompleted()
        best = min(best, (commands.gpuEndTime - commands.gpuStartTime) * 1000)
    }

    return best
}

func buffer(_ mode: MTLResourceOptions) -> MTLBuffer { device.makeBuffer(length: count * 16, options: mode)! }

let shared = (buffer(.storageModeShared), buffer(.storageModeShared))
let privates = (buffer(.storageModePrivate), buffer(.storageModePrivate))
print("\(device.name), \(count) float4 elements, GPU time in ms (best of 30)")

for round in 1...3 {
    let row = [
        ("shared→shared", best(shared.0, shared.1)),
        ("private→private", best(privates.0, privates.1)),
        ("shared→private", best(shared.0, privates.1)),
        ("private→shared", best(privates.0, shared.1)),
    ]
    print("round \(round): " + row.map { "\($0.0) \(String(format: "%.3f", $0.1))" }.joined(separator: ", "))
}

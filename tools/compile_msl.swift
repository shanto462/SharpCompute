// Compiles MSL source files with the Metal runtime compiler, using the same options as the
// SharpCompute runtime (MSL 3.2, safe math, precise FP functions). Needs a Metal device but no
// Xcode Metal toolchain.
//
// Verifies that every vector operator / intrinsic overload maps to valid MSL:
//
//     python3 -I tools/gen_vectors.py --msl-probe /tmp/probe.metal
//     swift tools/compile_msl.swift /tmp/probe.metal
//
// Prints OK/FAIL per file and exits non-zero if any file fails to compile.
import Foundation
import Metal

guard let device = MTLCreateSystemDefaultDevice() else {
    fputs("no Metal device\n", stderr)
    exit(2)
}
let options = MTLCompileOptions()
options.languageVersion = .version3_2
options.mathMode = .safe
options.mathFloatingPointFunctions = .precise

var failed = false
for path in CommandLine.arguments.dropFirst() {
    do {
        let source = try String(contentsOfFile: path, encoding: .utf8)
        _ = try device.makeLibrary(source: source, options: options)
        print("OK   \(path)")
    } catch {
        failed = true
        print("FAIL \(path)\n\(error)")
    }
}
exit(failed ? 1 : 0)

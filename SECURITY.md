# Security policy

## Supported versions

SharpCompute is pre-1.0 and has no releases or packages yet. Fixes go to `main` only, so check
whether a problem still reproduces on the latest `main` before you report it.

| Version | Supported |
|---|---|
| `main` | Yes |
| Anything older | No |

## Reporting a vulnerability

Report it privately through GitHub's private vulnerability reporting: on the repository's
**Security** tab, choose **Report a vulnerability**
([direct link](https://github.com/shanto462/SharpCompute/security/advisories/new)). Please don't
open a public issue, discussion or pull request for a vulnerability.

A useful report has the commit you tested, your macOS version and chip, what an attacker controls
(a shader's source, a file that is loaded, a buffer's contents), and the smallest reproduction you
have.

The project has one maintainer working on it in spare time, so responses are best effort: expect an
acknowledgement within about a week and an assessment soon after. A confirmed problem is fixed on
`main` and published as a GitHub security advisory, crediting you unless you prefer otherwise.

## Scope

In scope:

- **The source generator** (`SharpCompute.SourceGenerators`): C# code that makes it emit MSL that
  reads or writes outside the resources a shader was given, or that makes it misbehave inside the
  compiler process.
- **The runtime and its interop** (`SharpCompute`, `SharpCompute.Imaging`): memory-safety problems
  in the Objective-C and Metal interop, buffer, view and texture bounds checks that can be bypassed,
  use-after-free of native objects, and image files that make `SharpCompute.Imaging` read or write
  out of bounds.
- **The build task** (`SharpCompute.Build`): anything that runs commands or writes files beyond the
  shader library it produces.
- **The samples and scripts** in this repository.

Out of scope, and better filed as ordinary issues or with their owners:

- Bugs in macOS, Metal, the GPU driver or ImageIO themselves (report those to Apple).
- A shader that hangs or runs for a long time: a GPU program written by the application is trusted
  code, and macOS stops a command buffer that runs too long.
- Wrong results, crashes on invalid API use the documentation already rules out, and other
  non-security bugs: use the [issue tracker](https://github.com/shanto462/SharpCompute/issues).

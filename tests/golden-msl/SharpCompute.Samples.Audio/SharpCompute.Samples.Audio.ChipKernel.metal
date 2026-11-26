// SharpCompute.Samples.Audio.ChipKernel (SharpCompute.Samples.Audio): entry point SharpCompute_Samples_Audio_ChipKernel, compile options None
#include <metal_stdlib>

using namespace metal;

constant int BlockShift = 10;

struct VoiceEvent
{
    int StartSample;
    int Wave;
    uint Seed;
    int SpanSamples;
    float Gate;
    float StartHz;
    float EndHz;
    float VibratoHz;
    float VibratoDepth;
    float Duty;
    float DutySweep;
    float Volume;
    float Attack;
    float Decay;
    float Sustain;
    float Release;
};
static_assert(sizeof(VoiceEvent) == 64, "SharpCompute: size of 'VoiceEvent' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, StartSample) == 0, "SharpCompute: offset of 'VoiceEvent.StartSample' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Wave) == 4, "SharpCompute: offset of 'VoiceEvent.Wave' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Seed) == 8, "SharpCompute: offset of 'VoiceEvent.Seed' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, SpanSamples) == 12, "SharpCompute: offset of 'VoiceEvent.SpanSamples' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Gate) == 16, "SharpCompute: offset of 'VoiceEvent.Gate' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, StartHz) == 20, "SharpCompute: offset of 'VoiceEvent.StartHz' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, EndHz) == 24, "SharpCompute: offset of 'VoiceEvent.EndHz' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, VibratoHz) == 28, "SharpCompute: offset of 'VoiceEvent.VibratoHz' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, VibratoDepth) == 32, "SharpCompute: offset of 'VoiceEvent.VibratoDepth' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Duty) == 36, "SharpCompute: offset of 'VoiceEvent.Duty' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, DutySweep) == 40, "SharpCompute: offset of 'VoiceEvent.DutySweep' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Volume) == 44, "SharpCompute: offset of 'VoiceEvent.Volume' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Attack) == 48, "SharpCompute: offset of 'VoiceEvent.Attack' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Decay) == 52, "SharpCompute: offset of 'VoiceEvent.Decay' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Sustain) == 56, "SharpCompute: offset of 'VoiceEvent.Sustain' does not match its C# layout");
static_assert(__builtin_offsetof(VoiceEvent, Release) == 60, "SharpCompute: offset of 'VoiceEvent.Release' does not match its C# layout");

struct __Constants
{
    int sampleRate;
};
static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
static_assert(__builtin_offsetof(__Constants, sampleRate) == 0, "SharpCompute: offset of '__Constants.sampleRate' does not match its C# layout");

struct __GridWindow
{
    packed_uint3 offset;
    packed_uint3 size;
    packed_uint3 groupOffset;
};
static_assert(sizeof(__GridWindow) == 36, "SharpCompute: size of '__GridWindow' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, offset) == 0, "SharpCompute: offset of '__GridWindow.offset' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, size) == 12, "SharpCompute: offset of '__GridWindow.size' does not match its C# layout");
static_assert(__builtin_offsetof(__GridWindow, groupOffset) == 24, "SharpCompute: offset of '__GridWindow.groupOffset' does not match its C# layout");

constant bool __windowed [[function_constant(0)]];

static float HeldLevel(float t, float attack, float decay, float sustain);
static float EnvelopeLevel(float t, float gate, float attack, float decay, float sustain, float release);
static float ExpRatio(float x);
static float PolyBlep(float t, float dt);
static uint Hash(uint x);
static float Oscillator(int wave, float phase, float dt, float duty, uint seed);
static float VoiceSample(VoiceEvent e, int sample, int sampleRate);

static float HeldLevel(float t, float attack, float decay, float sustain)
{
    if (t < attack)
    {
        return t / attack;
    }
    float sinceDecay = t - attack;
    return sinceDecay < decay ? 1.0f + ((sustain - 1.0f) * (sinceDecay / decay)) : sustain;
}

static float EnvelopeLevel(float t, float gate, float attack, float decay, float sustain, float release)
{
    if (t < 0.0f)
    {
        return 0.0f;
    }
    float level = ::HeldLevel(metal::min(t, gate), attack, decay, sustain);
    if (t <= gate)
    {
        return level;
    }
    float sinceRelease = t - gate;
    return sinceRelease >= release ? 0.0f : level * (1.0f - (sinceRelease / release));
}

static float ExpRatio(float x)
{
    float u = metal::exp(x);
    return u == 1.0f ? 1.0f : (u - 1.0f) / metal::log(u);
}

static float PolyBlep(float t, float dt)
{
    if (t < dt)
    {
        float x = t / dt;
        return x + x - (x * x) - 1.0f;
    }
    if (t > 1.0f - dt)
    {
        float x = (t - 1.0f) / dt;
        return (x * x) + x + x + 1.0f;
    }
    return 0.0f;
}

static uint Hash(uint x)
{
    x ^= x >> 16;
    x *= 2146121005u;
    x ^= x >> 15;
    x *= 2221713035u;
    x ^= x >> 16;
    return x;
}

static float Oscillator(int wave, float phase, float dt, float duty, uint seed)
{
    float p = metal::fract(phase);
    if (wave <= 2)
    {
        float pulse = p < duty ? 1.0f : -1.0f;
        pulse += ::PolyBlep(p, dt) - ::PolyBlep(metal::fract(p - duty + 1.0f), dt);
        return (pulse - ((2.0f * duty) - 1.0f)) / (2.0f * metal::max(duty, 1.0f - duty));
    }
    if (wave == 3)
    {
        return 1.0f - (4.0f * metal::abs(metal::fract(phase + 0.25f) - 0.5f));
    }
    if (wave == 4)
    {
        float q = metal::fract(phase + 0.5f);
        return (2.0f * q) - 1.0f - ::PolyBlep(q, dt);
    }
    if (wave == 5)
    {
        return metal::sin(6.2831855f * p);
    }
    uint h = ::Hash(uint(metal::max(metal::floor(phase), 0.0f)) ^ (seed * 2654435769u));
    return (float(h >> 8) * (1.0f / 8388608.0f)) - 1.0f;
}

static float VoiceSample(VoiceEvent e, int sample, int sampleRate)
{
    int offset = int(uint(sample) - uint(e.StartSample));
    if (offset < 0 || offset >= e.SpanSamples)
    {
        return 0.0f;
    }
    float t = float(offset) / float(sampleRate);
    float level = ::EnvelopeLevel(t, e.Gate, e.Attack, e.Decay, e.Sustain, e.Release);
    if (level <= 0.0f)
    {
        return 0.0f;
    }
    float held = metal::min(t, e.Gate);
    float k = metal::log(e.EndHz / e.StartHz) / e.Gate;
    float frequency = e.StartHz * metal::exp(k * held);
    float phase = (e.StartHz * held * ::ExpRatio(k * held)) + (frequency * (t - held));
    float modulation = 1.0f;
    if (e.VibratoHz > 0.0f)
    {
        float w = 6.2831855f * e.VibratoHz;
        phase += e.VibratoDepth * frequency * (1.0f - metal::cos(w * t)) / w;
        modulation = 1.0f + (e.VibratoDepth * metal::sin(w * t));
    }
    float dt = metal::min(frequency * modulation / float(sampleRate), 0.5f);
    float duty = metal::clamp(e.Duty + (e.DutySweep * (held / e.Gate)), 0.05f, 0.95f);
    return ::Oscillator(e.Wave, phase, dt, duty, e.Seed) * level * e.Volume;
}

kernel void SharpCompute_Samples_Audio_ChipKernel(
    device float* output [[buffer(0)]],
    device VoiceEvent* events [[buffer(1)]],
    device int* blockStarts [[buffer(2)]],
    device int* blockEvents [[buffer(3)]],
    constant __Constants& __c [[buffer(4)]],
    constant __GridWindow& __window [[buffer(5), function_constant(__windowed)]],
    uint3 __tid_in [[thread_position_in_grid]])
{
    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
    int i = int(__tid.x);
    int block = i >> ::BlockShift;
    int last = blockStarts[int(uint(block) + 1u)];
    float sum = 0.0f;
    for (int k = blockStarts[block]; k < last; k = int(uint(k) + 1u))
    {
        sum += ::VoiceSample(events[blockEvents[k]], i, __c.sampleRate);
    }
    output[i] = metal::clamp(sum, -1.0f, 1.0f);
}

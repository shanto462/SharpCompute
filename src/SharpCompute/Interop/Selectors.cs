namespace SharpCompute.Interop;

/// <summary>Selectors registered once and cached for the lifetime of the process.</summary>
internal static class Selectors
{
    // NSObject
    public static readonly nint Alloc = ObjC.GetSelector("alloc");
    public static readonly nint Init = ObjC.GetSelector("init");
    public static readonly nint Retain = ObjC.GetSelector("retain");
    public static readonly nint Release = ObjC.GetSelector("release");

    // NSString
    public static readonly nint Length = ObjC.GetSelector("length");
    public static readonly nint InitWithCharactersLength = ObjC.GetSelector("initWithCharacters:length:");
    public static readonly nint GetCharactersRange = ObjC.GetSelector("getCharacters:range:");

    // NSError
    public static readonly nint LocalizedDescription = ObjC.GetSelector("localizedDescription");

    // MTLDevice
    public static readonly nint Name = ObjC.GetSelector("name");
    public static readonly nint MaxBufferLength = ObjC.GetSelector("maxBufferLength");
    public static readonly nint NewCommandQueue = ObjC.GetSelector("newCommandQueue");
    public static readonly nint NewBufferWithLengthOptions = ObjC.GetSelector("newBufferWithLength:options:");
    public static readonly nint NewBufferWithBytesLengthOptions = ObjC.GetSelector("newBufferWithBytes:length:options:");
    public static readonly nint NewBufferWithBytesNoCopyLengthOptionsDeallocator = ObjC.GetSelector("newBufferWithBytesNoCopy:length:options:deallocator:");
    public static readonly nint NewLibraryWithSourceOptionsError = ObjC.GetSelector("newLibraryWithSource:options:error:");
    public static readonly nint NewComputePipelineStateWithFunctionError = ObjC.GetSelector("newComputePipelineStateWithFunction:error:");
    public static readonly nint MinimumLinearTextureAlignmentForPixelFormat = ObjC.GetSelector("minimumLinearTextureAlignmentForPixelFormat:");

    // MTLCompileOptions
    public static readonly nint SetLanguageVersion = ObjC.GetSelector("setLanguageVersion:");
    public static readonly nint SetMathMode = ObjC.GetSelector("setMathMode:");
    public static readonly nint SetMathFloatingPointFunctions = ObjC.GetSelector("setMathFloatingPointFunctions:");

    // MTLLibrary
    public static readonly nint NewFunctionWithName = ObjC.GetSelector("newFunctionWithName:");

    // MTLComputePipelineState
    public static readonly nint MaxTotalThreadsPerThreadgroup = ObjC.GetSelector("maxTotalThreadsPerThreadgroup");

    // MTLBuffer
    public static readonly nint Contents = ObjC.GetSelector("contents");
    public static readonly nint NewTextureWithDescriptorOffsetBytesPerRow = ObjC.GetSelector("newTextureWithDescriptor:offset:bytesPerRow:");

    // MTLTextureDescriptor
    public static readonly nint SetTextureType = ObjC.GetSelector("setTextureType:");
    public static readonly nint SetPixelFormat = ObjC.GetSelector("setPixelFormat:");
    public static readonly nint SetWidth = ObjC.GetSelector("setWidth:");
    public static readonly nint SetHeight = ObjC.GetSelector("setHeight:");
    public static readonly nint SetUsage = ObjC.GetSelector("setUsage:");
    public static readonly nint SetStorageMode = ObjC.GetSelector("setStorageMode:");

    // MTLCommandQueue / MTLCommandBuffer
    public static readonly nint CommandBuffer = ObjC.GetSelector("commandBuffer");
    public static readonly nint ComputeCommandEncoder = ObjC.GetSelector("computeCommandEncoder");
    public static readonly nint Commit = ObjC.GetSelector("commit");
    public static readonly nint WaitUntilCompleted = ObjC.GetSelector("waitUntilCompleted");
    public static readonly nint Status = ObjC.GetSelector("status");
    public static readonly nint Error = ObjC.GetSelector("error");
    public static readonly nint AddCompletedHandler = ObjC.GetSelector("addCompletedHandler:");

    // MTLComputeCommandEncoder
    public static readonly nint SetComputePipelineState = ObjC.GetSelector("setComputePipelineState:");
    public static readonly nint SetBufferOffsetAtIndex = ObjC.GetSelector("setBuffer:offset:atIndex:");
    public static readonly nint SetBytesLengthAtIndex = ObjC.GetSelector("setBytes:length:atIndex:");
    public static readonly nint SetTextureAtIndex = ObjC.GetSelector("setTexture:atIndex:");
    public static readonly nint DispatchThreadsThreadsPerThreadgroup = ObjC.GetSelector("dispatchThreads:threadsPerThreadgroup:");
    public static readonly nint EndEncoding = ObjC.GetSelector("endEncoding");
}

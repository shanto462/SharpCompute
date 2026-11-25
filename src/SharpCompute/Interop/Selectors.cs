namespace SharpCompute.Interop;

/// <summary>Selectors registered once and cached for the lifetime of the process.</summary>
internal static class Selectors
{
    // NSObject
    public static readonly nint Alloc = ObjC.GetSelector("alloc");
    public static readonly nint Init = ObjC.GetSelector("init");
    public static readonly nint Retain = ObjC.GetSelector("retain");
    public static readonly nint Release = ObjC.GetSelector("release");
    public static readonly nint ConformsToProtocol = ObjC.GetSelector("conformsToProtocol:");

    // NSString
    public static readonly nint Length = ObjC.GetSelector("length");
    public static readonly nint InitWithCharactersLength = ObjC.GetSelector("initWithCharacters:length:");
    public static readonly nint GetCharactersRange = ObjC.GetSelector("getCharacters:range:");

    // NSError
    public static readonly nint LocalizedDescription = ObjC.GetSelector("localizedDescription");

    // MTLDevice
    public static readonly nint Name = ObjC.GetSelector("name");
    public static readonly nint MaxBufferLength = ObjC.GetSelector("maxBufferLength");
    public static readonly nint MaxThreadgroupMemoryLength = ObjC.GetSelector("maxThreadgroupMemoryLength");
    public static readonly nint NewCommandQueue = ObjC.GetSelector("newCommandQueue");
    public static readonly nint NewBufferWithLengthOptions = ObjC.GetSelector("newBufferWithLength:options:");
    public static readonly nint NewBufferWithBytesLengthOptions = ObjC.GetSelector("newBufferWithBytes:length:options:");
    public static readonly nint NewBufferWithBytesNoCopyLengthOptionsDeallocator = ObjC.GetSelector("newBufferWithBytesNoCopy:length:options:deallocator:");
    public static readonly nint NewLibraryWithSourceOptionsError = ObjC.GetSelector("newLibraryWithSource:options:error:");
    public static readonly nint NewComputePipelineStateWithFunctionError = ObjC.GetSelector("newComputePipelineStateWithFunction:error:");
    public static readonly nint MinimumLinearTextureAlignmentForPixelFormat = ObjC.GetSelector("minimumLinearTextureAlignmentForPixelFormat:");

    // MTLResource / MTLCommandQueue / MTLCommandBuffer (all have a device)
    public static readonly nint Device = ObjC.GetSelector("device");
    public static readonly nint StorageMode = ObjC.GetSelector("storageMode");
    public static readonly nint HazardTrackingMode = ObjC.GetSelector("hazardTrackingMode");

    // MTLTexture
    public static readonly nint TextureType = ObjC.GetSelector("textureType");
    public static readonly nint PixelFormat = ObjC.GetSelector("pixelFormat");
    public static readonly nint Width = ObjC.GetSelector("width");
    public static readonly nint Height = ObjC.GetSelector("height");
    public static readonly nint Depth = ObjC.GetSelector("depth");
    public static readonly nint ArrayLength = ObjC.GetSelector("arrayLength");
    public static readonly nint MipmapLevelCount = ObjC.GetSelector("mipmapLevelCount");
    public static readonly nint SampleCount = ObjC.GetSelector("sampleCount");
    public static readonly nint Usage = ObjC.GetSelector("usage");
    public static readonly nint Buffer = ObjC.GetSelector("buffer");
    public static readonly nint BufferOffset = ObjC.GetSelector("bufferOffset");
    public static readonly nint BufferBytesPerRow = ObjC.GetSelector("bufferBytesPerRow");
    public static readonly nint GetBytesFromRegion = ObjC.GetSelector("getBytes:bytesPerRow:fromRegion:mipmapLevel:");
    public static readonly nint ReplaceRegion = ObjC.GetSelector("replaceRegion:mipmapLevel:withBytes:bytesPerRow:");

    // MTLCompileOptions
    public static readonly nint SetLanguageVersion = ObjC.GetSelector("setLanguageVersion:");
    public static readonly nint SetMathMode = ObjC.GetSelector("setMathMode:");
    public static readonly nint SetMathFloatingPointFunctions = ObjC.GetSelector("setMathFloatingPointFunctions:");

    // MTLLibrary
    public static readonly nint NewFunctionWithName = ObjC.GetSelector("newFunctionWithName:");
    public static readonly nint NewFunctionWithNameConstantValuesError = ObjC.GetSelector("newFunctionWithName:constantValues:error:");

    // MTLFunctionConstantValues
    public static readonly nint SetConstantValueTypeAtIndex = ObjC.GetSelector("setConstantValue:type:atIndex:");

    // MTLComputePipelineState
    public static readonly nint MaxTotalThreadsPerThreadgroup = ObjC.GetSelector("maxTotalThreadsPerThreadgroup");
    public static readonly nint ThreadExecutionWidth = ObjC.GetSelector("threadExecutionWidth");

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
    public static readonly nint BlitCommandEncoder = ObjC.GetSelector("blitCommandEncoder");
    public static readonly nint Commit = ObjC.GetSelector("commit");
    public static readonly nint WaitUntilCompleted = ObjC.GetSelector("waitUntilCompleted");
    public static readonly nint Status = ObjC.GetSelector("status");
    public static readonly nint Error = ObjC.GetSelector("error");
    public static readonly nint AddCompletedHandler = ObjC.GetSelector("addCompletedHandler:");
    public static readonly nint GPUStartTime = ObjC.GetSelector("GPUStartTime");
    public static readonly nint GPUEndTime = ObjC.GetSelector("GPUEndTime");

    // MTLComputeCommandEncoder
    public static readonly nint SetComputePipelineState = ObjC.GetSelector("setComputePipelineState:");
    public static readonly nint SetBufferOffsetAtIndex = ObjC.GetSelector("setBuffer:offset:atIndex:");
    public static readonly nint SetBytesLengthAtIndex = ObjC.GetSelector("setBytes:length:atIndex:");
    public static readonly nint SetTextureAtIndex = ObjC.GetSelector("setTexture:atIndex:");
    public static readonly nint DispatchThreadsThreadsPerThreadgroup = ObjC.GetSelector("dispatchThreads:threadsPerThreadgroup:");
    public static readonly nint DispatchThreadgroupsWithIndirectBuffer = ObjC.GetSelector("dispatchThreadgroupsWithIndirectBuffer:indirectBufferOffset:threadsPerThreadgroup:");
    public static readonly nint EndEncoding = ObjC.GetSelector("endEncoding");

    // MTLBlitCommandEncoder
    public static readonly nint CopyFromBufferToBuffer = ObjC.GetSelector("copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size:");
    public static readonly nint FillBufferRangeValue = ObjC.GetSelector("fillBuffer:range:value:");
    public static readonly nint CopyFromTextureToTextureLevels = ObjC.GetSelector(
        "copyFromTexture:sourceSlice:sourceLevel:toTexture:destinationSlice:destinationLevel:sliceCount:levelCount:");
    public static readonly nint CopyFromBufferToTexture = ObjC.GetSelector(
        "copyFromBuffer:sourceOffset:sourceBytesPerRow:sourceBytesPerImage:sourceSize:toTexture:destinationSlice:destinationLevel:destinationOrigin:");
    public static readonly nint CopyFromTextureToBuffer = ObjC.GetSelector(
        "copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toBuffer:destinationOffset:destinationBytesPerRow:destinationBytesPerImage:");
}

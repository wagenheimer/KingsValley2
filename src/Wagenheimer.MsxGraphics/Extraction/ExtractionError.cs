using System.Text.Json;

namespace Wagenheimer.MsxGraphics.Extraction;

/// <summary>Stable English diagnostics for end-user reports, independent of OS exception localization.</summary>
public static class ExtractionError
{
    public static string Describe(Exception error) => error switch
    {
        FileNotFoundException => "A required file is missing.",
        DirectoryNotFoundException => "A required directory is missing.",
        UnauthorizedAccessException => "Access to an extraction file was denied.",
        EndOfStreamException => "The source file ended before its expected payload.",
        JsonException => "The JSON content is malformed or has an unexpected structure.",
        InvalidDataException => error.Message,
        NotSupportedException => error.Message,
        OperationCanceledException => "The extraction was cancelled.",
        ArgumentException => "Source values, dimensions or bounds are invalid.",
        IOException => "An extraction file could not be read or written.",
        _ => "Component processing failed (" + error.GetType().Name + ")."
    };
}

namespace VirCre.Launcher;

/// <summary>Expected transport, format and input failures that can be shown as a retryable catalog error.</summary>
public static class CatalogErrors {
    private static readonly object FormatSource=new();
    public static bool IsExpected(Exception error)=>error is IOException or InvalidDataException or TimeoutException or ArgumentException;
    public static InvalidDataException Format(string message,Exception? cause=null) {
        var error=new InvalidDataException(message,cause);
        error.Data[FormatSource]=true;
        return error;
    }
    public static bool IsFormatFailure(Exception error)=>error is InvalidDataException && error.Data[FormatSource] is true;
}

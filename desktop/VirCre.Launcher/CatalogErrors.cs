namespace VirCre.Launcher;

/// <summary>Expected transport, format and input failures that can be shown as a retryable catalog error.</summary>
public static class CatalogErrors {
    public static bool IsExpected(Exception error)=>error is IOException or InvalidDataException or TimeoutException or ArgumentException;
}

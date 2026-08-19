namespace Portal.Localization;

public static class LinguaExtensions
{
    public static string CurrentValue(this IObservable<string?> observable)
    {
        var observer = new CurrentValueObserver();
        using var subscription = observable.Subscribe(observer);
        return observer.Value ?? string.Empty;
    }

    private sealed class CurrentValueObserver : IObserver<string?>
    {
        public string? Value { get; private set; }

        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(string? value) => Value = value;
    }
}

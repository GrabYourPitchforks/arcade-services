﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.ReproTool;

public class AsyncDisposableValue<T> : IAsyncDisposable
{
    private Func<T, ValueTask> _dispose;

    public T Value { get; }

    internal AsyncDisposableValue(T value, Func<T, ValueTask> dispose)
    {
        Value = value;
        _dispose = dispose;
    }

    public ValueTask DisposeAsync()
    {
        Func<T, ValueTask> dispose = Interlocked.Exchange(ref _dispose!, null);
        return dispose?.Invoke(Value) ?? new ValueTask(Task.CompletedTask);
    }
}

public static class AsyncDisposableValue
{
    public static AsyncDisposableValue<T> Create<T>(T value, Func<T, ValueTask> dispose)
    {
        return new AsyncDisposableValue<T>(value, dispose);
    }

    public static AsyncDisposableValue<T> Create<T>(T value, Func<ValueTask> dispose)
    {
        return new AsyncDisposableValue<T>(value, _ => dispose());
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public static class Extensions
    {
        public static Task AsTask(this CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                return Task.FromException(
                    new ArgumentException("The passed in CancellationToken cannot be canceled", nameof(cancellationToken)));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>) s!).TrySetCanceled(cancellationToken), tcs);
            return tcs.Task;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Models;

public class Check
{
    public Check(CheckState status, string name, string url, bool isMaestroMergePolicy = false)
    {
        Status = status;
        Name = name;
        Url = url;
        IsMaestroMergePolicy = isMaestroMergePolicy;
    }

    public CheckState Status { get; }
    public string Name { get; }
    public string Url { get; }
    public bool IsMaestroMergePolicy { get; }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Models.GitHub;

public class GitHubComment
{
    public GitHubComment(string commentBody)
    {
        Body = commentBody;
    }

    public string Body { get; set; }
}

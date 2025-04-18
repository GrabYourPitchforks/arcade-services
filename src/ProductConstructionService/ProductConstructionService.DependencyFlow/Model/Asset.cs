// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.Serialization;

#nullable disable
namespace ProductConstructionService.DependencyFlow.Model;

[DataContract]
public class Asset
{
    [DataMember]
    public string Name { get; set; }

    [DataMember]
    public string Version { get; set; }

    [DataMember]
    public string[] Locations { get; set; }
}

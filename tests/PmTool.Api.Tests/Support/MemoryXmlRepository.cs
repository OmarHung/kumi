using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace PmTool.Api.Tests.Support;

/// <summary>只放在記憶體裡的金鑰存放庫，跟著應用程式實體一起消失。</summary>
internal sealed class MemoryXmlRepository : IXmlRepository
{
    private readonly ConcurrentQueue<XElement> _elements = new();

    public IReadOnlyCollection<XElement> GetAllElements() => [.. _elements.Select(element => new XElement(element))];

    public void StoreElement(XElement element, string friendlyName) => _elements.Enqueue(new XElement(element));
}

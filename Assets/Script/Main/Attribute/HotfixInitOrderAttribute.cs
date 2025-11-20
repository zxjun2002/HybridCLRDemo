using System;

/// <summary>
/// 只给热更 RuntimeInit 用的排序标签
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HotfixInitOrderAttribute : Attribute
{
    public int Order { get; }

    public HotfixInitOrderAttribute(int order)
    {
        Order = order;
    }
}
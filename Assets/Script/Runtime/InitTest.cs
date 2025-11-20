using UnityEngine;

public static class HotfixInitA
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    [HotfixInitOrder(0)]
    private static void InitA()
    {
        Debug.Log("[HotfixInitA] order 0");
    }
}

public static class HotfixInitB
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    [HotfixInitOrder(100)]
    private static void InitB()
    {
        Debug.Log("[HotfixInitB] order 100");
    }
}

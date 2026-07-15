using System.Runtime.InteropServices;
using Thalamus.Interop;

namespace Thalamus.Tests;

[TestClass]
public sealed class InteropContractTests
{
    [TestMethod]
    public void NativeStructLayoutsMatchWin32Contracts()
    {
        Assert.AreEqual(8, Marshal.SizeOf<NativeMethods.POINT>());
        Assert.AreEqual(16, Marshal.SizeOf<NativeMethods.RECT>());
        Assert.AreEqual(8, Marshal.SizeOf<NativeMethods.SIZE>());
        Assert.AreEqual(44, Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>());
        Assert.AreEqual(104, Marshal.SizeOf<NativeMethods.MONITORINFOEX>());
        Assert.AreEqual(48, Marshal.SizeOf<NativeMethods.DWM_THUMBNAIL_PROPERTIES>());
    }

    [TestMethod]
    public void ForwardingDeadlineExceedsPersistenceLockBudget()
    {
        Assert.IsTrue(
            global::Thalamus.App.CommandForwardTimeout > TimeSpan.FromSeconds(10));
    }
}
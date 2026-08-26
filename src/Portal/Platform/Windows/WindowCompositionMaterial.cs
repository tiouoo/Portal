// Portions derived from CompositionMaterial.Avalonia:
// https://github.com/HelloWRC/CompositionMaterial.Avalonia
// Copyright (c) 2026 HelloWRC, licensed under the MIT License.

using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Portal.Platform.Windows;

internal enum WindowCompositionMaterialKind
{
    Acrylic,
    Mica
}

internal sealed class WindowCompositionMaterial : IDisposable
{
    private const string SupportedAvaloniaVersion = "12.1.1.0";

    private readonly TopLevel _topLevel;
    private readonly Assembly _win32Assembly;
    private readonly object _syncRoot;
    private readonly object _compositor;
    private readonly object _rootVisual;
    private readonly object _rootContainer;
    private readonly object _rootChildren;
    private readonly object _sprite;
    private readonly object _visual;
    private readonly object _clipGeometry;
    private readonly Type _visualType;
    private readonly Type _spriteVisualType;
    private readonly Type _visualCollectionType;
    private readonly Type _winUiUtilsType;
    private readonly IntPtr _windowHandle;
    private readonly int _previousHostBackdrop;
    private readonly bool _restoreHostBackdrop;
    private object? _brush;
    private bool _disposed;

    private WindowCompositionMaterial(TopLevel topLevel, Assembly win32Assembly, object syncRoot, object compositor,
        object rootVisual, object rootContainer, object rootChildren, object sprite, object visual,
        object clipGeometry, Type visualType, Type spriteVisualType, Type visualCollectionType, Type winUiUtilsType)
    {
        _topLevel = topLevel;
        _win32Assembly = win32Assembly;
        _syncRoot = syncRoot;
        _compositor = compositor;
        _rootVisual = rootVisual;
        _rootContainer = rootContainer;
        _rootChildren = rootChildren;
        _sprite = sprite;
        _visual = visual;
        _clipGeometry = clipGeometry;
        _visualType = visualType;
        _spriteVisualType = spriteVisualType;
        _visualCollectionType = visualCollectionType;
        _winUiUtilsType = winUiUtilsType;
        _windowHandle = topLevel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && _windowHandle != IntPtr.Zero)
        {
            DwmGetWindowAttribute(_windowHandle, DwmwaUseHostBackdropBrush, out _previousHostBackdrop, sizeof(int));
            var enabled = 1;
            _restoreHostBackdrop = DwmSetWindowAttribute(_windowHandle, DwmwaUseHostBackdropBrush, ref enabled,
                sizeof(int)) == 0;
        }
    }

    public static WindowCompositionMaterial? TryCreate(TopLevel topLevel)
    {
        if (!OperatingSystem.IsWindows()) return null;

        object? rootVisual = null;
        object? rootContainer = null;
        object? rootChildren = null;
        object? sprite = null;
        object? visual = null;
        object? clipGeometry = null;

        try
        {
            var win32Assembly = Assembly.Load("Avalonia.Win32");
            if (win32Assembly.GetName().Version?.ToString() != SupportedAvaloniaVersion) return null;

            var platform = topLevel.PlatformImpl ?? throw new InvalidOperationException("Window platform is unavailable.");
            var surface = FindField(platform.GetType(), "_glSurface").GetValue(platform)
                          ?? throw new InvalidOperationException("Window composition surface is unavailable.");
            var surfaceType = RequireType(win32Assembly,
                "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindowSurface");
            if (!surfaceType.IsInstanceOfType(surface)) return null;

            var windowType = RequireType(win32Assembly,
                "Avalonia.Win32.WinRT.Composition.WinUiCompositedWindow");
            var window = RequireField(surfaceType, "_window").GetValue(surface)
                         ?? throw new InvalidOperationException("WinUI composited window is unavailable.");
            var target = RequireField(windowType, "_target").GetValue(window)
                         ?? throw new InvalidOperationException("Composition target is unavailable.");
            var shared = RequireField(windowType, "_shared").GetValue(window)
                         ?? throw new InvalidOperationException("Shared compositor is unavailable.");
            var swapchainVisual = RequireField(windowType, "_visual").GetValue(window)
                                  ?? throw new InvalidOperationException("Swapchain visual is unavailable.");

            var sharedType = RequireType(win32Assembly, "Avalonia.Win32.WinRT.Composition.WinUiCompositionShared");
            var compositor = RequireProperty(sharedType, "Compositor").GetValue(shared)
                             ?? throw new InvalidOperationException("Compositor is unavailable.");
            var syncRoot = RequireProperty(sharedType, "SyncRoot").GetValue(shared)
                           ?? throw new InvalidOperationException("Compositor lock is unavailable.");

            var targetType = RequireType(win32Assembly, "Avalonia.Win32.WinRT.ICompositionTarget");
            rootVisual = RequireProperty(targetType, "Root").GetValue(target)
                         ?? throw new InvalidOperationException("Root visual is unavailable.");
            var containerVisualType = RequireType(win32Assembly, "Avalonia.Win32.WinRT.IContainerVisual");
            rootContainer = QueryInterface(rootVisual, containerVisualType);
            rootChildren = RequireProperty(containerVisualType, "Children").GetValue(rootContainer)
                           ?? throw new InvalidOperationException("Root visual collection is unavailable.");

            var compositorType = RequireType(win32Assembly, "Avalonia.Win32.WinRT.ICompositor");
            sprite = RequireMethod(compositorType, "CreateSpriteVisual", 0).Invoke(compositor, null)
                     ?? throw new InvalidOperationException("Unable to create material sprite.");
            var visualType = RequireType(win32Assembly, "Avalonia.Win32.WinRT.IVisual");
            visual = QueryInterface(sprite, visualType);
            var visualCollectionType = RequireType(win32Assembly, "Avalonia.Win32.WinRT.IVisualCollection");

            lock (syncRoot)
                RequireMethod(visualCollectionType, "InsertBelow", 2).Invoke(rootChildren,
                    [visual, swapchainVisual]);

            var winUiUtilsType = RequireType(win32Assembly,
                "Avalonia.Win32.WinRT.Composition.WinUiCompositionUtils");
            var visualArray = Array.CreateInstance(visualType, 1);
            visualArray.SetValue(visual, 0);
            clipGeometry = winUiUtilsType.GetMethod("ClipVisual",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.Invoke(null, [compositor, (float?)0, visualArray])
                ?? throw new InvalidOperationException("Unable to clip material sprite.");

            return new WindowCompositionMaterial(topLevel, win32Assembly, syncRoot, compositor, rootVisual,
                rootContainer, rootChildren, sprite, visual, clipGeometry, visualType,
                RequireType(win32Assembly, "Avalonia.Win32.WinRT.ISpriteVisual"), visualCollectionType,
                winUiUtilsType);
        }
        catch
        {
            DisposeProxy(clipGeometry);
            DisposeProxy(visual);
            DisposeProxy(sprite);
            DisposeProxy(rootChildren);
            DisposeProxy(rootContainer);
            DisposeProxy(rootVisual);
            return null;
        }
    }

    public bool Apply(WindowCompositionMaterialKind kind, double cornerRadius)
    {
        if (_disposed) return false;

        object? brush = null;
        try
        {
            brush = kind == WindowCompositionMaterialKind.Mica ? CreateMicaBrush() : CreateAcrylicBrush();
            if (brush is null) return false;

            lock (_syncRoot)
            {
                RequireMethod(_spriteVisualType, "SetBrush", 1).Invoke(_sprite, [brush]);
                DisposeProxy(_brush);
                _brush = brush;
                brush = null;
                if (!UpdateGeometry(cornerRadius))
                    throw new InvalidOperationException("Unable to size the material sprite.");
                RequireMethod(_visualType, "SetOpacity", 1).Invoke(_visual, [1f]);
                RequireMethod(_visualType, "SetIsVisible", 1).Invoke(_visual, [1]);
            }

            return true;
        }
        catch
        {
            DisposeProxy(brush);
            return false;
        }
    }

    public bool UpdateGeometry(double cornerRadius)
    {
        if (_disposed) return false;
        try
        {
            var scaling = _topLevel.RenderScaling;
            var size = new Vector2((float)(_topLevel.ClientSize.Width * scaling),
                (float)(_topLevel.ClientSize.Height * scaling));
            var radius = new Vector2((float)(Math.Max(0, cornerRadius) * scaling));
            var geometryType = RequireType(_win32Assembly,
                "Avalonia.Win32.WinRT.ICompositionRoundedRectangleGeometry");

            lock (_syncRoot)
            {
                RequireMethod(_visualType, "SetSize", 1).Invoke(_visual, [size]);
                RequireMethod(geometryType, "SetSize", 1).Invoke(_clipGeometry, [size]);
                RequireMethod(geometryType, "SetCornerRadius", 1).Invoke(_clipGeometry, [radius]);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Hide()
    {
        if (_disposed) return;
        try
        {
            lock (_syncRoot)
                RequireMethod(_visualType, "SetIsVisible", 1).Invoke(_visual, [0]);
        }
        catch
        {
            // The composition target may already be shutting down.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_syncRoot)
        {
            try
            {
                RequireMethod(_visualCollectionType, "Remove", 1).Invoke(_rootChildren, [_visual]);
            }
            catch
            {
                // The native visual tree can disappear before Window.Closed is raised.
            }
        }

        DisposeProxy(_brush);
        DisposeProxy(_clipGeometry);
        DisposeProxy(_visual);
        DisposeProxy(_sprite);
        DisposeProxy(_rootChildren);
        DisposeProxy(_rootContainer);
        DisposeProxy(_rootVisual);
        if (_restoreHostBackdrop && _windowHandle != IntPtr.Zero)
        {
            var previous = _previousHostBackdrop;
            DwmSetWindowAttribute(_windowHandle, DwmwaUseHostBackdropBrush, ref previous, sizeof(int));
        }
    }

    private object? CreateAcrylicBrush()
    {
        return _winUiUtilsType.GetMethod("CreateAcrylicBlurBackdropBrush",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(null, [_compositor]);
    }

    private object? CreateMicaBrush()
    {
        var dark = _topLevel.ActualThemeVariant == ThemeVariant.Dark;
        var method = _winUiUtilsType.GetMethod("CreateMicaBackdropBrush",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return method?.Invoke(null, [_compositor, dark ? 32f : 242f, dark ? 0.8f : 0.6f])
               ?? CreateAcrylicBrush();
    }

    private static object QueryInterface(object value, Type interfaceType)
    {
        var microComRuntime = Type.GetType("MicroCom.Runtime.MicroComRuntime, MicroCom.Runtime", true)!;
        var unknownType = Type.GetType("MicroCom.Runtime.IUnknown, MicroCom.Runtime", true)!;
        if (!unknownType.IsInstanceOfType(value))
            throw new InvalidOperationException($"{value.GetType()} is not a MicroCom proxy.");
        var method = microComRuntime.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => candidate.Name == "QueryInterface" && candidate.IsGenericMethodDefinition);
        return method.MakeGenericMethod(interfaceType).Invoke(null, [value])
               ?? throw new InvalidOperationException($"QueryInterface({interfaceType.Name}) returned null.");
    }

    private static Type RequireType(Assembly assembly, string name) => assembly.GetType(name, true)!;

    private static FieldInfo FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null) return field;
        }

        throw new MissingFieldException(type.FullName, name);
    }

    private static FieldInfo RequireField(Type type, string name) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(type.FullName, name);

    private static PropertyInfo RequireProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingMemberException(type.FullName, name);

    private static MethodInfo RequireMethod(Type type, string name, int parameterCount) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == name && method.GetParameters().Length == parameterCount);

    private static void DisposeProxy(object? value)
    {
        if (value is IDisposable disposable) disposable.Dispose();
    }

    private const int DwmwaUseHostBackdropBrush = 17;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

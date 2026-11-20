#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using Nez.Textures;
using KingsValley.Core.Common;
using KingsValley.Engine.Graphics;
using Wagenheimer.MonoGameHelper.UI;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Base of **all** game scenes.
///
/// ## Common setup (so no scene forgets it)
///
/// 1. **Native resolution** + **design** coordinate space (1430x768) — see `AGENTS.md`.
/// 2. **Camera in the design model** (<see cref="DesignViewportCamera"/>), applied **before**
///    anything that captures the camera's rest position (e.g.: camera shake).
/// 3. **Post-UI overlay cleanup** from previous scenes.
///
/// Nez calls <see cref="Initialize"/> inside the scene's **constructor**, so it is `sealed`.
/// The child scenes implement <see cref="OnInitialize"/>.
///
/// ## Resource management (important)
///
/// Nez gives each scene its own `ContentManager` (`Scene.Content`) whose resources are released
/// on `Unload`. **However** "raw" loading (`Texture2D.FromStream`, used by this project's
/// atlases and backgrounds) is **not** tracked by it — without care, that leaks VRAM.
///
/// Use <see cref="Own{T}"/> when creating any disposable resource outside `Content`:
///
/// <code>
/// var textura = Own(Texture2D.FromStream(GraphicsDevice, stream));
/// </code>
///
/// On <see cref="Unload"/> the scene releases everything automatically (newest to oldest).
/// If a child scene overrides <see cref="Unload"/>, **call `base.Unload()`**.
/// </summary>
public abstract class SceneBase : Scene
{
    private readonly List<IDisposable> _owned = new();

    /// <summary>
    /// Common setup. `sealed` on purpose: make sure every scene goes through the same path.
    ///
    /// ⚠️ Nez calls <see cref="Initialize"/> inside the scene's **base constructor** — that is,
    /// this method runs **before** the child scene's constructor body. That is why content that
    /// depends on the scene's fields/parameters is NOT created here: that is <see cref="OnStart"/>'s job.
    /// </summary>
    public sealed override void Initialize()
    {
        base.Initialize();

        // 1. NATIVE resolution (RT = backbuffer, 1:1 blit) keeping Unity's design coordinate
        //    space (1430x768). The mapping is done by CAMERA ZOOM — see DesignViewport.
        //    Do NOT go back to FixedHeight: it renders everything at 768 and stretches afterwards, blurring the image.
        SetDesignResolution(
            GameConstants.DesignWidth,
            GameConstants.DesignHeight,
            SceneResolutionPolicy.None);

        // MSX2 pixel art: POINT filter (crisp), same as Nez.Core.DefaultSamplerState.
        SamplerState = SamplerState.PointClamp;
        ClearColor = Color.Black;

        // 2. Camera in the design model — BEFORE any component that captures the rest
        //    position (e.g.: CinematicCameraShake).
        Camera?.Entity?.AddComponent(new DesignViewportCamera());

        // 3. Safety net for the post-UI overlay.
        PostUiOverlay.Clear();
    }

    /// <summary>
    /// Scene content. Runs when the scene **starts**, after the child scene's constructor has
    /// assigned its fields — it is here (and not in <see cref="Initialize"/>) that entities are created,
    /// assets are loaded and the constructor parameters are read.
    /// </summary>
    public sealed override void OnStart()
    {
        base.OnStart();
        OnInitialize();

        // Diagnostic (only with KINGSVALLEY_I18N_DEBUG=1): reports the keys THIS scene asked for and that
        // do not exist in the language nor in the default. Runs AFTER OnInitialize builds the UI.
        Localization.LogMissingKeys();
    }

    /// <summary>Content specific to the scene. Called by <see cref="OnStart"/>.</summary>
    protected abstract void OnInitialize();

    /// <summary>
    /// Registers a manually created resource to be released when the scene unloads.
    /// Returns the resource itself, for inline use: `var t = Own(Texture2D.FromStream(...));`
    /// </summary>
    protected T Own<T>(T disposable) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(disposable);
        _owned.Add(disposable);
        return disposable;
    }

    /// <summary>Loads a texture from a file and already registers the automatic release.</summary>
    protected Texture2D LoadTexture(string relativePath)
    {
        var fullPath = ResolveTexturePath(relativePath);

        using var stream = System.IO.File.OpenRead(fullPath);
        var texture = Texture2D.FromStream(Nez.Core.GraphicsDevice, stream);
        return Own(texture);
    }

    /// <summary>
    /// Loads a **premultiplied** texture (RGB already multiplied by the alpha).
    ///
    /// ## When to use this one and not <see cref="LoadTexture"/>
    ///
    /// It depends on the **blend state of whoever draws**:
    ///
    /// | Who draws | Blend | Texture |
    /// | :--- | :--- | :--- |
    /// | Nez scene (Batcher), `PostUiOverlay` | `BlendState.AlphaBlend` (premultiplied) | **this one** |
    /// | Gum | `BlendState.NonPremultiplied` | <see cref="LoadTexture"/> |
    ///
    /// `Texture2D.FromStream` **does not** premultiply. With `AlphaBlend` that leaves a **light halo**
    /// on every semi-transparent PNG edge (the RGB of the transparent pixels comes in at full
    /// strength) — that is what happened with the composed logo. The board atlas already solved that
    /// by passing `premultiplyAlpha: true` in the Nez loader (see `BoardAssetCatalog`).
    /// </summary>
    protected Texture2D LoadTexturePremultiplied(string relativePath)
    {
        var fullPath = ResolveTexturePath(relativePath);

        using var stream = System.IO.File.OpenRead(fullPath);
        var texture = TextureUtils.TextureFromStreamPreMultiplied(stream);
        return Own(texture);
    }

    private static string ResolveTexturePath(string relativePath)
    {
        var fullPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (!System.IO.File.Exists(fullPath))
            fullPath = relativePath;

        return fullPath;
    }

    /// <summary>
    /// Releases the resources registered via <see cref="Own{T}"/>.
    /// If a child scene overrides it, **call `base.Unload()`**.
    /// </summary>
    public override void Unload()
    {
        // Releases from newest to oldest (dependencies first).
        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            try
            {
                _owned[i].Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SceneBase] Failed to release resource: {ex.Message}");
            }
        }

        _owned.Clear();
        base.Unload();
    }

    /// <summary>
    /// Called when the graphics variant/art set is switched at runtime (e.g. via F11 or the system menu).
    /// Child scenes override this to hot-reload their textures/backgrounds in-place without restarting the scene.
    /// </summary>
    public virtual void OnGfxVariantChanged()
    {
    }
}

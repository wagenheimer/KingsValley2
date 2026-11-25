#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using KingsValley.Engine.Navigation;
using KingsValley.Engine.UI;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// **Time-sliced** loading: runs the <see cref="LoadStep"/> within a budget of
/// ~6 ms per frame, keeping the bar animating and the window responsive.
///
/// The <c>GraphicsDevice</c> is not thread-safe, so `Task.Run` does not help — that is why the
/// work is sliced, not parallelized.
/// </summary>
public class LoadingScene : SceneBase
{
    private readonly GameSceneId _target;
    private readonly List<LoadStep> _steps;
    private readonly float _minimumDuration;

    /// <summary>Constructor without steps (used by the generic navigation).</summary>
    public LoadingScene() : this(GameSceneId.MainMenu, null, 0.5f)
    {
    }

    public LoadingScene(GameSceneId target, IEnumerable<LoadStep>? steps = null, float minimumDuration = 0.9f)
    {
        _target = target;
        _steps = steps != null ? new List<LoadStep>(steps) : new List<LoadStep>();
        _minimumDuration = minimumDuration;
    }

    protected override void OnInitialize()
    {
        AddEntity(new Entity("loading")).AddComponent(new LoadingOverlay(_steps, _minimumDuration, _target));
    }

    private sealed class LoadingOverlay : ScreenOverlay
    {
        private const float BudgetMs = 6f;

        private readonly List<LoadStep> _steps;
        private readonly float _minimumDuration;
        private readonly GameSceneId _target;
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        private int _index;
        private float _elapsed;
        private bool _done;

        public LoadingOverlay(List<LoadStep> steps, float minimumDuration, GameSceneId target)
        {
            _steps = steps;
            _minimumDuration = minimumDuration;
            _target = target;
        }

        public override void Update()
        {
            float dt = Time.DeltaTime;
            _elapsed += dt;

            if (_done)
                return;

            // Runs steps while there is a time budget in this frame.
            while (_index < _steps.Count)
            {
                var step = _steps[_index];
                try
                {
                    step.Work();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Loading] step '{step.Name}' failed: {ex.Message}");
                }

                _index++;

                if (_watch.Elapsed.TotalMilliseconds >= BudgetMs)
                {
                    _watch.Restart();
                    break;
                }
            }

            bool stepsDone = _index >= _steps.Count;
            if (stepsDone && _elapsed >= _minimumDuration)
            {
                _done = true;
                GameNavigation.GoTo(_target);
            }
        }

        public override void DrawPostUi(SpriteBatch batch)
        {
            Fill(batch, 0, 0, DesignW, DesignH, new Color(12, 12, 18));

            // Bar: 70% from the step progress + 30% from the minimum duration.
            float stepProgress = _steps.Count == 0 ? 1f : _index / (float)_steps.Count;
            float timeProgress = Math.Clamp(_elapsed / _minimumDuration, 0f, 1f);
            float progress = Math.Clamp(stepProgress * 0.7f + timeProgress * 0.3f, 0f, 1f);

            float w = DesignW * 0.6f;
            float x = (DesignW - w) * 0.5f;
            float y = DesignH * 0.5f;

            Fill(batch, x, y, w, 10, new Color(60, 55, 45));
            Fill(batch, x, y, w * progress, 10, new Color(230, 200, 90));
        }
    }
}

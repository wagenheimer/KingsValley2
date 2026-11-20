#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Wagenheimer.MonoGameHelper.Localization;

namespace KingsValley.Engine;

/// <summary>
/// Engine localization service: holds the active <see cref="IStringLocalizer"/> and resolves the
/// initial culture from the save (or the OS).
///
/// The localizer itself is agnostic and lives in the <c>Wagenheimer.MonoGameHelper</c> package
/// (an <c>AGENTS.md</c> rule). Only the **glue** stays here: read the save, find the content folder and
/// apply the culture at boot. Before <see cref="Initialize"/> the <see cref="Loc"/> is a
/// <see cref="NullLocalizer"/> (returns the key itself), so nothing breaks if the content is missing.
/// </summary>
public static class Localization
{
    /// <summary>Translations directory used at initialization (null while not initialized).</summary>
    private static string? _directory;

    /// <summary>Language name -> code index (e.g.: "Portuguese (Brazil)" -> "pt-BR").</summary>
    private static IReadOnlyDictionary<string, string>? _nameIndex;

    /// <summary>Languages of manifest.json in file order (empty if the content is missing).</summary>
    private static IReadOnlyList<(string Code, string Name)>? _languages;

    private static readonly object ManifestLock = new();

    /// <summary>Active localizer (never null; <see cref="NullLocalizer"/> before Initialize).</summary>
    public static IStringLocalizer Loc { get; private set; } = NullLocalizer.Instance;

    /// <summary>Code of the active culture (e.g.: <c>"pt-BR"</c>).</summary>
    public static string Culture => Loc.Culture;

    /// <summary>Indicates whether <see cref="Initialize"/> has already run (even if the content was missing).</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// Turns on the missing-keys diagnostic (env <c>HOPESFARM_I18N_DEBUG=1</c>). Evaluated a single
    /// time at boot: in release, without the variable, <see cref="LogMissingKeys"/> is a no-op (0 overhead).
    /// </summary>
    public static bool DebugMissingKeys { get; } =
        Environment.GetEnvironmentVariable("HOPESFARM_I18N_DEBUG") == "1";

    /// <summary>
    /// Supported languages (code + name) read from <c>manifest.json</c>, in file order.
    /// Stays **empty** when the localization content is not available (<see cref="NullLocalizer"/>).
    /// </summary>
    public static IReadOnlyList<(string Code, string Name)> Languages
    {
        get
        {
            EnsureManifest();
            return _languages!;
        }
    }

    /// <summary>
    /// Creates the <see cref="JsonStringLocalizer"/> from
    /// <paramref name="contentDirectory"/>/Localization and applies the culture (from the save or the OS).
    /// Idempotent: new calls only reapply the culture.
    /// </summary>
    /// <param name="contentDirectory">Game <c>Content</c> folder (the <c>Localization</c> subfolder is appended).</param>
    /// <param name="savedLanguage">Language stored in the save (code or name); null/empty detects from the OS.</param>
    public static void Initialize(string contentDirectory, string? savedLanguage)
    {
        if (IsInitialized)
        {
            SetCulture(ResolveCulture(savedLanguage));
            return;
        }

        var directory = string.IsNullOrWhiteSpace(contentDirectory)
            ? null
            : Path.Combine(contentDirectory, "Localization");

        if (directory == null || !Directory.Exists(directory) ||
            !Directory.EnumerateFiles(directory, "*.json").Any())
        {
            Loc = NullLocalizer.Instance;
            IsInitialized = true;
            Console.WriteLine(
                $"[Localization] warning: no translation JSON in '{directory}'. Using NullLocalizer.");
            return;
        }

        _directory = directory;

        // Discards any manifest cache read before (e.g.: if `Languages` was touched without
        // content). From here on the index and the list come from this directory.
        _nameIndex = null;
        _languages = null;

        Loc = new JsonStringLocalizer(directory, "en");
        SetCulture(ResolveCulture(savedLanguage));
        IsInitialized = true;
    }

    /// <summary>
    /// Resolves the supported culture from the save language; if absent, detects from the OS.
    /// Accepts the **code** ("pt-BR") or the manifest language **name** ("Portuguese (Brazil)").
    /// An unknown value falls back to the default (<c>"en"</c>).
    /// </summary>
    /// <param name="savedLanguage">Value of <c>SaveGameData.GameLanguage</c>.</param>
    /// <returns>Canonical code of a supported culture.</returns>
    public static string ResolveCulture(string? savedLanguage)
    {
        var codes = Loc.AvailableCultures;
        var fallback = string.IsNullOrWhiteSpace(Loc.DefaultCulture) ? "en" : Loc.DefaultCulture;

        if (!string.IsNullOrWhiteSpace(savedLanguage))
        {
            var value = savedLanguage.Trim();

            var byCode = MatchCode(value, codes);
            if (byCode != null)
            {
                return byCode;
            }

            var byName = MatchName(value);
            if (byName != null)
            {
                return byName;
            }

            return fallback;
        }

        return DetectFromOs(codes, fallback);
    }

    /// <summary>Switches the culture at runtime (an unknown code falls back to the localizer's default).</summary>
    /// <param name="code">Culture code, e.g.: <c>"ja"</c>.</param>
    public static void SetCulture(string code) => Loc.SetCulture(code);

    /// <summary>
    /// Logs a summary of the missing keys (<see cref="IStringLocalizer.MissingKeys"/>): the count and,
    /// if any, up to 20 examples. <b>No-op</b> when <see cref="DebugMissingKeys"/> is <c>false</c>.
    /// Called at the end of each scene's start, to report what that screen asked for.
    /// </summary>
    public static void LogMissingKeys()
    {
        if (!DebugMissingKeys)
        {
            return;
        }

        var missing = Loc.MissingKeys;
        Console.WriteLine($"[i18n] missing keys: {missing.Count}");
        if (missing.Count == 0)
        {
            return;
        }

        foreach (var key in missing.Take(20))
        {
            Console.WriteLine($"[i18n]   {key}");
        }

        if (missing.Count > 20)
        {
            Console.WriteLine($"[i18n] (showing 20 of {missing.Count})");
        }
    }

    /// <summary>
    /// Unity/TMP rich text markup present in some I2 terms
    /// (e.g.: <c>&lt;color=white&gt;</c>, <c>&lt;/color&gt;</c>, <c>&lt;sprite name=coin&gt;</c>).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex MarkupRegex =
        new("<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Removes the Unity/TMP rich text markup from a text already resolved by I2 — Gum
    /// draws the tags literally (e.g.: "&lt;color=white&gt;Name&lt;/color&gt;"). Use only on the
    /// messages whose I2 term brings those tags.
    /// </summary>
    /// <param name="text">Text resolved by the localizer.</param>
    /// <returns>The text without the rich text tags.</returns>
    public static string Plain(string text) => MarkupRegex.Replace(text, string.Empty);

    /// <summary>Matches <paramref name="value"/> with a supported code (case-insensitive).</summary>
    private static string? MatchCode(string value, IReadOnlyCollection<string> codes)
    {
        foreach (var code in codes)
        {
            if (string.Equals(code, value, StringComparison.OrdinalIgnoreCase))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>Matches <paramref name="value"/> with the name of a manifest.json language.</summary>
    private static string? MatchName(string value)
    {
        var index = NameIndex();
        return index.TryGetValue(value, out var code) && !string.IsNullOrWhiteSpace(code) ? code : null;
    }

    /// <summary>Detecta a cultura a partir de <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    private static string DetectFromOs(IReadOnlyCollection<string> codes, string fallback)
    {
        CultureInfo ui;
        try
        {
            ui = CultureInfo.CurrentUICulture;
        }
        catch (Exception)
        {
            return fallback;
        }

        // 1. Exact code (e.g.: "pt-BR").
        var exact = MatchCode(ui.Name, codes);
        if (exact != null)
        {
            return exact;
        }

        var two = ui.TwoLetterISOLanguageName;

        // 2. Chinese: distinguishes simplified (Hans/zh-CN) from traditional (Hant/zh-TW).
        if (string.Equals(two, "zh", StringComparison.OrdinalIgnoreCase))
        {
            var name = ui.Name;
            var traditional = name.IndexOf("Hant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
                              name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase);

            return MatchCode(traditional ? "zh-TW" : "zh-CN", codes) ?? fallback;
        }

        // 3. 2-letter prefix (e.g.: "pt" -> "pt-BR", "ja" -> "ja").
        if (!string.IsNullOrEmpty(two))
        {
            foreach (var code in codes)
            {
                if (string.Equals(Prefix(code), two, StringComparison.OrdinalIgnoreCase))
                {
                    return code;
                }
            }
        }

        return fallback;
    }

    /// <summary>Part before the hyphen of the code (<c>"pt-BR"</c> -> <c>"pt"</c>).</summary>
    private static string Prefix(string code)
    {
        var separator = code.IndexOf('-');
        return separator > 0 ? code.Substring(0, separator) : code;
    }

    /// <summary>Name -> code index (lazy, cached) from manifest.json.</summary>
    private static IReadOnlyDictionary<string, string> NameIndex()
    {
        EnsureManifest();
        return _nameIndex!;
    }

    /// <summary>
    /// Reads (once) the <c>manifest.json</c> and populates the name -> code index and the ordered list of
    /// languages. Silent when the directory/manifest does not exist: both stay empty.
    /// </summary>
    private static void EnsureManifest()
    {
        if (_nameIndex != null)
        {
            return;
        }

        lock (ManifestLock)
        {
            if (_nameIndex != null)
            {
                return;
            }

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var languages = new List<(string Code, string Name)>();
            try
            {
                if (_directory != null)
                {
                    var manifestPath = Path.Combine(_directory, "manifest.json");
                    if (File.Exists(manifestPath))
                    {
                        var manifest = JsonSerializer.Deserialize<LocalizationManifest>(
                            File.ReadAllText(manifestPath));

                        if (manifest?.Languages != null)
                        {
                            foreach (var language in manifest.Languages)
                            {
                                if (string.IsNullOrWhiteSpace(language.Name) ||
                                    string.IsNullOrWhiteSpace(language.Code))
                                {
                                    continue;
                                }

                                index[language.Name] = language.Code;
                                languages.Add((language.Code, language.Name));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Localization] warning: failed to read manifest.json: {ex.Message}");
            }

            _nameIndex = index;
            _languages = languages;
        }
    }

    /// <summary>
    /// Minimal <see cref="IStringLocalizer"/> implementation that returns the key itself.
    /// Used while the localization content is not available (does not break the game).
    /// </summary>
    private sealed class NullLocalizer : IStringLocalizer
    {
        /// <summary>Single instance (stateless).</summary>
        public static readonly NullLocalizer Instance = new();

        private NullLocalizer()
        {
        }

        /// <inheritdoc />
        public string this[string key] => key;

        /// <inheritdoc />
        public string Format(string key, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, key, args);

        /// <inheritdoc />
        public string Culture => "en";

        /// <inheritdoc />
        public string DefaultCulture => "en";

        /// <inheritdoc />
        public IReadOnlyCollection<string> AvailableCultures => Array.Empty<string>();

        /// <inheritdoc />
        public IReadOnlyCollection<string> MissingKeys => Array.Empty<string>();

        /// <inheritdoc />
        public void SetCulture(string culture)
        {
        }
    }
}

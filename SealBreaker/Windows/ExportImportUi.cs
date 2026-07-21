using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SealBreaker.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SealBreaker.Windows;

/// <summary>
/// The six buy-list export/import windows (confirmation, toast, import review,
/// invalid string, success, merge conflicts). Drawn from MainWindow.Draw each
/// frame; state machine driven by BeginExport/BeginImport.
/// </summary>
public sealed class ExportImportUi
{
    private enum Stage { None, ExportConfirm, ImportConfirm, MergeConflict, InvalidString, Success }

    private Stage _stage = Stage.None;

    // Export state
    private List<ClassifiedEntry> _classified = [];
    private int _exportGc;

    // Toast state
    private DateTime? _toastAt;
    private Vector2 _toastAnchor;
    private string _toastCharacter = string.Empty;
    private int _toastGc;
    private string _toastSummary = string.Empty;

    // Import state
    private TransferPayload _payload = new();
    private ImportResolution _resolution = new();
    private int _recipientGc;
    private MergePlan _mergePlan = new();
    private bool[] _mergeUseImported = [];
    private string _invalidError = string.Empty;

    // Success summary
    private bool _successWasMerge;
    private int _successAdded;
    private int _successSkipped;
    private int _successExcluded;
    private int _successMapped;
    private int _successSourceGc;

    private static Vector4 GcColor(int gcIdx) => gcIdx switch
    {
        0 => UiTheme.Red,
        1 => UiTheme.Yellow,
        _ => UiTheme.Accent,
    };

    // ── Entry points (called from the Buy list section) ───────

    public void BeginExport(Configuration cfg)
    {
        _exportGc = cfg.GrandCompanyIndex;
        var list = cfg.GcShopBuyListFor(_exportGc);
        if (list.Count == 0)
        {
            _invalidError = "Your buy list is empty — nothing to export.";
            _stage = Stage.InvalidString;
            return;
        }

        GcShopCatalog.EnsureInitialized();
        _classified = BuyListTransfer.Classify(list, _exportGc, GcShopCatalog.Entries);
        _stage = Stage.ExportConfirm;
    }

    public void BeginImport(Configuration cfg)
    {
        string? clipboard = null;
        try { clipboard = ImGui.GetClipboardText(); }
        catch { /* clipboard unavailable */ }

        if (!BuyListTransfer.TryParse(clipboard, out _payload, out var error))
        {
            _invalidError = $"The clipboard does not contain a SealBreaker export — {error}.";
            _stage = Stage.InvalidString;
            return;
        }

        _recipientGc = cfg.GrandCompanyIndex;
        GcShopCatalog.EnsureInitialized();
        _resolution = BuyListTransfer.Resolve(_payload, _recipientGc, GcShopCatalog.Entries);
        _stage = Stage.ImportConfirm;
    }

    public void Draw(Configuration cfg)
    {
        switch (_stage)
        {
            case Stage.ExportConfirm: DrawExportConfirm(cfg); break;
            case Stage.ImportConfirm: DrawImportConfirm(cfg); break;
            case Stage.MergeConflict: DrawMergeConflict(cfg); break;
            case Stage.InvalidString: DrawInvalidString(); break;
            case Stage.Success: DrawSuccess(); break;
        }

        DrawToast();
    }

    // ── Shared pieces ─────────────────────────────────────────

    private static bool ColoredHeader(string label, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(color.X, color.Y, color.Z, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(color.X, color.Y, color.Z, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(color.X, color.Y, color.Z, 0.24f));
        var open = ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PopStyleColor(4);
        return open;
    }

    private static string CharacterLabel()
    {
        try
        {
            var player = Service.ObjectTable.LocalPlayer;
            if (player == null)
                return "Unknown";

            var world = player.HomeWorld.ValueNullable?.Name.ExtractText();
            return string.IsNullOrWhiteSpace(world) ? player.Name.TextValue : $"{player.Name.TextValue}@{world}";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string PluginVersion()
    {
        try
        {
            return typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    // ── Window 1: Export confirmation ─────────────────────────

    private void DrawExportConfirm(Configuration cfg)
    {
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(480, 430), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Export buy list — review###SbExportConfirm", ref open))
        {
            var clean = _classified.Where(c => c.Kind == TransferKind.Universal).ToList();
            var mapped = _classified.Where(c => c.Kind == TransferKind.Mapped).ToList();
            var excluded = _classified.Where(c => c.Kind == TransferKind.Exclusive).ToList();

            ImGui.TextColored(UiTheme.Gray, "Exporting the");
            ImGui.SameLine(0, 6);
            ImGui.TextColored(GcColor(_exportGc), GrandCompanyState.GrandCompanyName(_exportGc));
            ImGui.SameLine(0, 6);
            ImGui.TextColored(UiTheme.Gray, "buy list as a shareable preset.");
            UiTheme.GoldFadeRule();

            if (ColoredHeader($"✓ Transfers cleanly ({clean.Count} items)", UiTheme.Green) && clean.Count > 0)
            {
                if (ImGui.BeginTable("##exportClean", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX))
                {
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Keep", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                    foreach (var item in clean)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextColored(UiTheme.TextBright, item.CatalogEntry!.ItemName);
                        ImGui.TableNextColumn();
                        ImGui.TextColored(UiTheme.Gray, item.Entry.KeepAmount.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextColored(UiTheme.Gray, item.Entry.BuyQtyPerPurchase == 0 ? "max" : item.Entry.BuyQtyPerPurchase.ToString());
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.Spacing();
            if (ColoredHeader($"→ GC name mapped ({mapped.Count} items)", UiTheme.Yellow) && mapped.Count > 0)
            {
                foreach (var item in mapped)
                {
                    ImGui.TextColored(UiTheme.TextBright, $"  {item.CatalogEntry!.ItemName}");
                    ImGui.SameLine(0, 6);
                    ImGui.TextColored(UiTheme.Teal, "→");
                    ImGui.SameLine(0, 6);
                    ImGui.TextColored(UiTheme.Yellow, item.CounterpartSummary ?? "recipient's equivalent");
                }
            }

            ImGui.Spacing();
            if (ColoredHeader($"✗ Cannot transfer ({excluded.Count} items)", UiTheme.Red) && excluded.Count > 0)
            {
                foreach (var item in excluded)
                {
                    ImGui.TextColored(UiTheme.Gray, $"  {item.Entry.ItemName}");
                    ImGui.SameLine(0, 8);
                    ImGui.TextColored(UiTheme.Red, $"— {item.ExcludeReason}");
                }
            }

            ImGui.Spacing();
            ImGui.TextColored(UiTheme.Yellow,
                "GC-specific items are exported as generic references — whoever imports\n" +
                "this gets their own Grand Company's equivalent automatically.");
            ImGui.Spacing();

            if (ImGui.Button("Cancel##export", new Vector2(100, 26)))
                _stage = Stage.None;
            ImGui.SameLine();

            var exportable = clean.Count + mapped.Count;
            if (exportable == 0) ImGui.BeginDisabled();
            if (UiTheme.SolidButton("Export to clipboard", UiTheme.GreenDark, new Vector2(160, 26)))
            {
                var text = BuyListTransfer.BuildExportString(_classified, _exportGc, CharacterLabel(), PluginVersion());
                ImGui.SetClipboardText(text);
                Service.PluginLog.Information($"[SealBreaker] Exported buy list: {clean.Count} clean, {mapped.Count} mapped, {excluded.Count} excluded");

                _toastAt = DateTime.UtcNow;
                _toastAnchor = ImGui.GetMousePos() + new Vector2(16, -12);
                _toastCharacter = CharacterLabel();
                _toastGc = _exportGc;
                _toastSummary = $"{clean.Count} clean · {mapped.Count} mapped · {excluded.Count} excluded";
                _stage = Stage.None;
            }

            if (exportable == 0)
            {
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextColored(UiTheme.Red, "nothing transferable");
            }
        }

        ImGui.End();
        if (!open)
            _stage = Stage.None;
    }

    // ── Window 2: Export success toast ────────────────────────

    private void DrawToast()
    {
        if (_toastAt is not { } shownAt)
            return;

        if ((DateTime.UtcNow - shownAt).TotalSeconds > 4)
        {
            _toastAt = null;
            return;
        }

        ImGui.SetNextWindowPos(_toastAnchor, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.96f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoScrollbar;
        if (ImGui.Begin("###SbExportToast", flags))
        {
            UiTheme.Icon(FontAwesomeIcon.CheckCircle, UiTheme.Green);
            ImGui.SameLine(0, 6);
            ImGui.TextColored(UiTheme.TextBright, "Configuration copied to clipboard");
            ImGui.TextColored(UiTheme.Gray, $"{_toastCharacter} —");
            ImGui.SameLine(0, 5);
            ImGui.TextColored(GcColor(_toastGc), GrandCompanyState.GrandCompanyName(_toastGc));
            ImGui.TextColored(UiTheme.Gray, _toastSummary);

            if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                _toastAt = null;
        }

        ImGui.End();
    }

    // ── Window 3: Import confirmation ─────────────────────────

    private void DrawImportConfirm(Configuration cfg)
    {
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(500, 480), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Import buy list — review###SbImportConfirm", ref open))
        {
            ImGui.TextColored(UiTheme.Gray, "From");
            ImGui.SameLine(0, 6);
            var sourceLabel = string.IsNullOrWhiteSpace(_payload.Source.GcName)
                ? GrandCompanyState.GrandCompanyName(_payload.Source.Gc)
                : _payload.Source.GcName;
            ImGui.TextColored(GcColor(_payload.Source.Gc), sourceLabel);
            if (!string.IsNullOrWhiteSpace(_payload.Source.Character))
            {
                ImGui.SameLine(0, 6);
                ImGui.TextColored(UiTheme.NavHeader, $"({_payload.Source.Character})");
            }

            ImGui.SameLine(0, 8);
            ImGui.TextColored(UiTheme.Teal, "→");
            ImGui.SameLine(0, 8);
            ImGui.TextColored(GcColor(_recipientGc), GrandCompanyState.GrandCompanyName(_recipientGc));
            ImGui.SameLine(0, 6);
            ImGui.TextColored(UiTheme.Gray, "(this character)");
            UiTheme.GoldFadeRule();

            if (ColoredHeader($"✓ Transfers cleanly ({_resolution.Clean.Count} items)", UiTheme.Green) && _resolution.Clean.Count > 0)
            {
                if (ImGui.BeginTable("##importClean", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX))
                {
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Keep", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                    foreach (var item in _resolution.Clean)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextColored(UiTheme.TextBright, item.Entry.ItemName);
                        ImGui.TableNextColumn();
                        ImGui.TextColored(UiTheme.Gray, item.Entry.KeepAmount.ToString());
                        ImGui.TableNextColumn();
                        ImGui.TextColored(UiTheme.Gray, item.Entry.BuyQtyPerPurchase == 0 ? "max" : item.Entry.BuyQtyPerPurchase.ToString());
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.Spacing();
            if (ColoredHeader($"→ GC name mapped ({_resolution.Mapped.Count} items)", UiTheme.Yellow) && _resolution.Mapped.Count > 0)
            {
                foreach (var item in _resolution.Mapped)
                {
                    ImGui.TextColored(UiTheme.Gray, $"  {item.MappedFromName}");
                    ImGui.SameLine(0, 6);
                    ImGui.TextColored(UiTheme.Teal, "→");
                    ImGui.SameLine(0, 6);
                    ImGui.TextColored(GcColor(_recipientGc), item.Entry.ItemName);
                }
            }

            ImGui.Spacing();
            if (ColoredHeader($"✗ Cannot transfer ({_resolution.Excluded.Count} items)", UiTheme.Red) && _resolution.Excluded.Count > 0)
            {
                foreach (var (name, reason) in _resolution.Excluded)
                {
                    ImGui.TextColored(UiTheme.Gray, $"  {name}");
                    ImGui.SameLine(0, 8);
                    ImGui.TextColored(UiTheme.Red, $"— {reason}");
                }
            }

            ImGui.Spacing();
            var existing = cfg.GcShopBuyListFor(_recipientGc);
            if (existing.Count > 0)
                ImGui.TextColored(UiTheme.Red,
                    $"Replace wipes your current {GrandCompanyState.GrandCompanyName(_recipientGc)} buy list ({existing.Count} entries).");
            ImGui.Spacing();

            if (ImGui.Button("Cancel##import", new Vector2(90, 26)))
                _stage = Stage.None;
            ImGui.SameLine();

            var importable = _resolution.Clean.Count + _resolution.Mapped.Count;
            if (importable == 0) ImGui.BeginDisabled();

            if (UiTheme.SolidButton("Merge", UiTheme.GreenDark, new Vector2(90, 26)))
                StartMerge(cfg);
            ImGui.SameLine();
            if (UiTheme.SolidButton("Replace", UiTheme.RedDark, new Vector2(90, 26)))
            {
                var target = cfg.GcShopBuyListFor(_recipientGc);
                BuyListTransfer.ApplyReplace(target, _resolution.AllEntries());
                cfg.Save();
                ShowSuccess(merge: false, added: importable, skipped: 0);
            }

            if (importable == 0)
            {
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextColored(UiTheme.Red, "nothing importable");
            }

            ImGui.Spacing();
            ImGui.TextColored(UiTheme.Gray, "Merge keeps everything you already have and adds the new entries;");
            ImGui.TextColored(UiTheme.Gray, "conflicts ask you per item. Replace deletes your list first, then imports.");
        }

        ImGui.End();
        if (!open)
            _stage = Stage.None;
    }

    private void StartMerge(Configuration cfg)
    {
        var existing = cfg.GcShopBuyListFor(_recipientGc);
        _mergePlan = BuyListTransfer.PlanMerge(existing, _resolution.AllEntries());

        if (_mergePlan.Conflicts.Count == 0)
        {
            BuyListTransfer.ApplyMerge(existing, _mergePlan, []);
            cfg.Save();
            ShowSuccess(merge: true, added: _mergePlan.Added.Count, skipped: _mergePlan.SkippedDuplicates);
            return;
        }

        _mergeUseImported = new bool[_mergePlan.Conflicts.Count];
        _stage = Stage.MergeConflict;
    }

    private void ShowSuccess(bool merge, int added, int skipped)
    {
        _successWasMerge = merge;
        _successAdded = added;
        _successSkipped = skipped;
        _successExcluded = _resolution.Excluded.Count;
        _successMapped = _resolution.Mapped.Count;
        _successSourceGc = _payload.Source.Gc;
        _stage = Stage.Success;
        Service.PluginLog.Information(
            $"[SealBreaker] Imported buy list ({(merge ? "merge" : "replace")}): +{added}, skipped {skipped}, excluded {_successExcluded}, mapped {_successMapped}");
    }

    // ── Window 4: Invalid string ──────────────────────────────

    private void DrawInvalidString()
    {
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(440, 210), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Import failed###SbImportInvalid", ref open))
        {
            UiTheme.Icon(FontAwesomeIcon.TimesCircle, UiTheme.Red);
            ImGui.SameLine(0, 8);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 380);
            ImGui.TextColored(UiTheme.Red, _invalidError);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            ImGui.TextColored(UiTheme.Gray, "A valid export is one line that starts with:");
            ImGui.TextColored(UiTheme.Teal, $"  {BuyListTransfer.Prefix}eyJ2IjoxLCJzb3VyY2UiOn...");
            ImGui.Spacing();
            ImGui.TextColored(UiTheme.Gray, "Ask the sender to click Export to clipboard again and re-paste —");
            ImGui.TextColored(UiTheme.Gray, "partial copies and extra characters are the usual cause.");
            ImGui.Spacing();

            if (UiTheme.SolidButton("OK##invalid", UiTheme.CardBorder, new Vector2(90, 26)))
                _stage = Stage.None;
        }

        ImGui.End();
        if (!open)
            _stage = Stage.None;
    }

    // ── Window 5: Import success ──────────────────────────────

    private void DrawSuccess()
    {
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(440, 240), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Import complete###SbImportSuccess", ref open))
        {
            UiTheme.Icon(FontAwesomeIcon.CheckCircle, UiTheme.Green);
            ImGui.SameLine(0, 8);
            ImGui.TextColored(UiTheme.Green, "Buy list imported");
            UiTheme.GoldFadeRule();

            ImGui.TextColored(UiTheme.TextBright, _successWasMerge
                ? $"{_successAdded} entries added"
                : $"{_successAdded} entries imported (list replaced)");
            if (_successWasMerge)
                ImGui.TextColored(UiTheme.Gray, $"{_successSkipped} duplicate(s) skipped");
            ImGui.TextColored(UiTheme.Gray, $"{_successExcluded} excluded (no counterpart in your GC)");
            ImGui.Spacing();

            if (_successMapped > 0)
            {
                ImGui.TextColored(UiTheme.Yellow, $"{_successMapped} item(s) mapped from");
                ImGui.SameLine(0, 5);
                ImGui.TextColored(GcColor(_successSourceGc), GrandCompanyState.GrandCompanyName(_successSourceGc));
                ImGui.SameLine(0, 5);
                ImGui.TextColored(UiTheme.Yellow, "names to your");
                ImGui.SameLine(0, 5);
                ImGui.TextColored(GcColor(_recipientGc), GrandCompanyState.GrandCompanyName(_recipientGc));
                ImGui.SameLine(0, 5);
                ImGui.TextColored(UiTheme.Yellow, "equivalents.");
            }

            ImGui.Spacing();
            if (UiTheme.SolidButton("OK##success", UiTheme.GreenDark, new Vector2(90, 26)))
                _stage = Stage.None;
        }

        ImGui.End();
        if (!open)
            _stage = Stage.None;
    }

    // ── Window 6: Merge conflicts ─────────────────────────────

    private void DrawMergeConflict(Configuration cfg)
    {
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(540, 300), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Merge conflicts###SbMergeConflict", ref open))
        {
            ImGui.TextColored(UiTheme.Gray, "These items exist in both lists with different settings — pick per item.");
            ImGui.Spacing();

            if (ImGui.Button("Keep all current"))
                Array.Fill(_mergeUseImported, false);
            ImGui.SameLine();
            if (ImGui.Button("Use all imported"))
                Array.Fill(_mergeUseImported, true);

            ImGui.Spacing();
            if (ImGui.BeginTable("##mergeConflicts", 4,
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX))
            {
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Current", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Imported", ImGuiTableColumnFlags.WidthFixed, 90);
                ImGui.TableSetupColumn("Choice", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableHeadersRow();

                for (var i = 0; i < _mergePlan.Conflicts.Count; i++)
                {
                    var conflict = _mergePlan.Conflicts[i];
                    ImGui.PushID(i);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(UiTheme.TextBright, conflict.Current.ItemName);
                    ImGui.TableNextColumn();
                    ImGui.TextColored(!_mergeUseImported[i] ? UiTheme.Teal : UiTheme.Gray, FormatEntry(conflict.Current));
                    ImGui.TableNextColumn();
                    ImGui.TextColored(_mergeUseImported[i] ? UiTheme.Teal : UiTheme.Gray, FormatEntry(conflict.Incoming));
                    ImGui.TableNextColumn();
                    if (UiTheme.SolidButton("Current", !_mergeUseImported[i] ? UiTheme.GreenDark : UiTheme.CardBorder, new Vector2(68, 20)))
                        _mergeUseImported[i] = false;
                    ImGui.SameLine(0, 4);
                    if (UiTheme.SolidButton("Imported", _mergeUseImported[i] ? UiTheme.GreenDark : UiTheme.CardBorder, new Vector2(72, 20)))
                        _mergeUseImported[i] = true;
                    ImGui.PopID();
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            if (ImGui.Button("Cancel##merge", new Vector2(90, 26)))
                _stage = Stage.None;
            ImGui.SameLine();
            if (UiTheme.SolidButton("Confirm merge", UiTheme.GreenDark, new Vector2(120, 26)))
            {
                var target = cfg.GcShopBuyListFor(_recipientGc);
                BuyListTransfer.ApplyMerge(target, _mergePlan, _mergeUseImported);
                cfg.Save();
                ShowSuccess(merge: true, added: _mergePlan.Added.Count, skipped: _mergePlan.SkippedDuplicates);
            }
        }

        ImGui.End();
        if (!open)
            _stage = Stage.None;
    }

    private static string FormatEntry(GcShopBuyEntry entry) =>
        $"keep {entry.KeepAmount} · qty {(entry.BuyQtyPerPurchase == 0 ? "max" : entry.BuyQtyPerPurchase.ToString())}{(entry.Enabled ? "" : " · off")}";
}

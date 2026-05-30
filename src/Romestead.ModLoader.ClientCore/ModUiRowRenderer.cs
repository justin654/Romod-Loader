using Candide.CandideUI;
using Candide.CandideUI.Components;
using Candide.CandideUI.Components.Buttons;
using Candide.CandideUI.Containers;
using Candide.CandideUI.Input;
using Candide.CandideUI.PauseMenuUi;
using CandideCreator.Shared.Graphics;
using Microsoft.Xna.Framework;
using Romestead.ModLoader;

namespace Romestead.ModLoader.ClientCore;

/// <summary>
/// Translates the declarative section/row model (<see cref="ModSection"/> / <see cref="ModUiRow"/>)
/// into Candide UI elements. Shared by the overlay and window hosts so both render the same row
/// vocabulary, including the crafting-oriented rows (image, button bar, item-slot grid, text input).
///
/// Callers supply a <c>contextFactory</c> that builds the <see cref="ModUiActionContext"/> handed to
/// row callbacks; each host wires its own dirty/navigation behaviour into that context.
/// </summary>
internal static class ModUiRowRenderer
{
    public static void RenderRow(
        CandideVerticalStackPanel panel,
        ModUiRow row,
        Func<ModUiActionContext> contextFactory)
    {
        switch (row)
        {
            case ModLabelRow label:
                panel.AddChild(CreateLabel(label.Text, MapStyle(label.Style), bottomMargin: 4), false);
                break;

            case ModInfoRow info:
                panel.AddChild(CreateLabel(info.Label, CandideTextStyle.MainBold, bottomMargin: 2), false);
                panel.AddChild(CreateLabel(info.Value, MapStyle(info.Style), bottomMargin: 8, leftMargin: 12), false);
                break;

            case ModListRow list:
                panel.AddChild(CreateLabel(list.Label, CandideTextStyle.MainBold, bottomMargin: 4), false);
                if (list.Values.Count == 0)
                {
                    panel.AddChild(CreateLabel(list.EmptyText, CandideTextStyle.MainSmall, bottomMargin: 8, leftMargin: 12), false);
                }
                else
                {
                    foreach (var value in list.Values)
                    {
                        panel.AddChild(CreateLabel(value, CandideTextStyle.MainSmall, bottomMargin: 4, leftMargin: 12), false);
                    }
                }

                break;

            case ModProgressRow progress:
                if (!string.IsNullOrWhiteSpace(progress.Label))
                {
                    panel.AddChild(CreateLabel(progress.Label!, CandideTextStyle.MainBold, bottomMargin: 4), false);
                }

                panel.AddChild(CreateLabel(BuildProgressBar(progress.Fraction), CandideTextStyle.MainSmall, bottomMargin: 8), false);
                break;

            case ModButtonRow button:
                panel.AddChild(
                    CreateButton(button.Label, () => InvokeSafe(button.Label, () => button.OnClick(contextFactory()))),
                    false);
                break;

            case ModNavigateRow navigate:
                panel.AddChild(
                    CreateButton(navigate.Label, () => InvokeSafe(navigate.Label, () => contextFactory().NavigateToPage(navigate.TargetPageId))),
                    false);
                break;

            case ModToggleRow toggle:
                panel.AddChild(
                    CandideUiSettingsHelper.CreateCheckboxSetting(
                        toggle.Value,
                        toggle.Label,
                        toggle.Description ?? "",
                        enabled => InvokeSafe(toggle.Label, () => toggle.OnChanged(contextFactory(), enabled))),
                    false);
                break;

            case ModImageRow image:
                panel.AddChild(BuildImageRow(image), false);
                break;

            case ModButtonBarRow buttonBar:
                panel.AddChild(BuildButtonBar(buttonBar, contextFactory), false);
                break;

            case ModItemSlotGridRow grid:
                BuildItemSlotGrid(panel, grid);
                break;

            case ModTextInputRow input:
                BuildTextInput(panel, input, contextFactory);
                break;

            default:
                panel.AddChild(CreateLabel($"Unsupported row: {row.GetType().Name}", CandideTextStyle.MainSmall, bottomMargin: 4), false);
                break;
        }
    }

    private static CandideUiElement BuildImageRow(ModImageRow image)
    {
        var icon = new CandideIcon(MapIconSize(image.Size))
        {
            Icon = image.IconId,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new CandideThickness { Bottom = string.IsNullOrWhiteSpace(image.Caption) ? 8 : 4 }
        };

        if (string.IsNullOrWhiteSpace(image.Caption))
        {
            return icon;
        }

        var stack = new CandideVerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new CandideThickness { Bottom = 8 }
        };
        stack.AddChild(icon, false);
        stack.AddChild(CreateLabel(image.Caption!, CandideTextStyle.MainSmall, bottomMargin: 0), false);
        return stack;
    }

    private static CandideUiElement BuildButtonBar(ModButtonBarRow bar, Func<ModUiActionContext> contextFactory)
    {
        var row = new CandideHorizontalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new CandideThickness { Bottom = 8 }
        };

        foreach (var button in bar.Buttons)
        {
            var width = Math.Max(60, button.Width);
            row.AddChild(
                new NoIconLabelButton(width)
                {
                    Text = button.Label,
                    Width = width,
                    Margin = new CandideThickness { Right = 6 },
                    OnClickAction = () => InvokeSafe(button.Label, () => button.OnClick(contextFactory()))
                },
                false);
        }

        return row;
    }

    private static void BuildItemSlotGrid(CandideVerticalStackPanel panel, ModItemSlotGridRow grid)
    {
        if (!string.IsNullOrWhiteSpace(grid.Label))
        {
            panel.AddChild(CreateLabel(grid.Label!, CandideTextStyle.MainBold, bottomMargin: 4), false);
        }

        if (grid.Slots.Count == 0)
        {
            panel.AddChild(CreateLabel(grid.EmptyText, CandideTextStyle.MainSmall, bottomMargin: 8, leftMargin: 12), false);
            return;
        }

        var columns = Math.Max(1, grid.Columns);
        CandideHorizontalStackPanel? currentRow = null;

        for (var i = 0; i < grid.Slots.Count; i++)
        {
            if (i % columns == 0)
            {
                currentRow = new CandideHorizontalStackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new CandideThickness { Bottom = 6 }
                };
                panel.AddChild(currentRow, false);
            }

            currentRow!.AddChild(BuildItemSlot(grid.Slots[i]), false);
        }
    }

    private static CandideUiElement BuildItemSlot(ModItemSlot slot)
    {
        var cell = new CandideVerticalStackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new CandideThickness { Right = 8 }
        };

        cell.AddChild(
            new CandideIcon(IconFlag.Medium)
            {
                Icon = slot.IconId,
                HorizontalAlignment = HorizontalAlignment.Center
            },
            false);

        if (slot.Count > 1)
        {
            var countLabel = CreateLabel($"x{slot.Count}", CandideTextStyle.MainSmall, bottomMargin: 0);
            countLabel.HorizontalAlignment = HorizontalAlignment.Center;
            cell.AddChild(countLabel, false);
        }

        if (!string.IsNullOrWhiteSpace(slot.Caption))
        {
            cell.AddChild(CreateLabel(slot.Caption!, CandideTextStyle.MainSmall, bottomMargin: 0), false);
        }

        return cell;
    }

    private static void BuildTextInput(
        CandideVerticalStackPanel panel,
        ModTextInputRow input,
        Func<ModUiActionContext> contextFactory)
    {
        if (!string.IsNullOrWhiteSpace(input.Label))
        {
            panel.AddChild(CreateLabel(input.Label!, CandideTextStyle.MainBold, bottomMargin: 4), false);
        }

        var box = new CandideTextInputBox
        {
            Text = input.Value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 32,
            Margin = new CandideThickness { Bottom = 8 }
        };

        if (input.MaxLength is int max)
        {
            box.MaxLength = max;
        }

        if (!string.IsNullOrWhiteSpace(input.Placeholder))
        {
            box.HintText = input.Placeholder;
            box.HintTextEnabled = true;
        }

        box.TextChangedByUser += (_, _) =>
            InvokeSafe(input.Label ?? "text input", () => input.OnChanged(contextFactory(), box.Text ?? ""));

        panel.AddChild(box, false);
    }

    public static string BuildProgressBar(double? fraction)
    {
        const int slots = 20;

        if (fraction is null)
        {
            return "[" + new string('.', slots) + "]";
        }

        var clamped = Math.Clamp(fraction.Value, 0.0, 1.0);
        var filled = (int)Math.Round(clamped * slots);
        var bar = new string('#', filled) + new string('.', slots - filled);
        return $"[{bar}] {clamped * 100:0}%";
    }

    public static CandideTextLabel CreateLabel(string text, CandideTextStyle style, int bottomMargin, int leftMargin = 0)
    {
        return new CandideTextLabel
        {
            Text = text,
            TextStyle = style,
            Wrap = true,
            ShadowColor = Color.Black,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness { Left = leftMargin, Bottom = bottomMargin }
        };
    }

    public static CandideHorizontalSeparator CreateSeparator()
    {
        return new CandideHorizontalSeparator
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new CandideThickness { Top = 4, Bottom = 8 }
        };
    }

    public static NoIconLabelButton CreateButton(string text, Action onClick)
    {
        return new NoIconLabelButton(220)
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new CandideThickness { Bottom = 8 },
            OnClickAction = onClick
        };
    }

    public static CandideTextStyle MapStyle(ModUiTextStyle style) =>
        style switch
        {
            ModUiTextStyle.Title => CandideTextStyle.TitleBold,
            ModUiTextStyle.BodyStrong => CandideTextStyle.MainBold,
            _ => CandideTextStyle.MainSmall
        };

    private static IconFlag MapIconSize(ModUiIconSize size) =>
        size switch
        {
            ModUiIconSize.Small => IconFlag.Small,
            ModUiIconSize.Large => IconFlag.Large,
            ModUiIconSize.Huge => IconFlag.Huge,
            _ => IconFlag.Medium
        };

    public static void InvokeSafe(string actionName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CoreState.Logger?.Error($"A mod UI callback threw for action '{actionName}'.", ex);
        }
    }
}

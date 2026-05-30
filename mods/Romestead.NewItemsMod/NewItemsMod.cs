using Romestead.ModLoader;

namespace Romestead.NewItemsMod;

[ModManifest("romestead.new-items", "Romestead New Items Mod", "0.1.0")]
public sealed class NewItemsMod : IRomesteadMod, IContentMod
{
    private const string SettingsPageId = "romestead.new-items.settings";
    private const string SidebarEntryId = "romestead.new-items.sidebar";
    internal const string ItemId = "material:mod:ember_resin";
    private const string ItemIconId = "icon_ember_resin";
    private const string EmbercraftStationId = "embercraft";
    private const string ItemNameTextId = "item.romestead.new-items.ember_resin.name";
    private const string ItemDescriptionTextId = "item.romestead.new-items.ember_resin.description";
    private const string SkillId = "skill:mod:embercraft";
    private const string SkillNameTextId = "skill.romestead.new-items.embercraft.name";
    private const string SkillDescriptionTextId = "skill.romestead.new-items.embercraft.description";
    private const string PlayerClassNameTextId = "player_class.romestead.new-items.emberwright.name";

    private const string EmberSwordId = "weapon:mod:ember_sword";
    private const string EmberSwordDisplayId = "cdd:mod:ember_sword";
    private const string EmberSwordSkinName = "RomesteadNewItemsEmberSword";
    private const string EmberSwordNameTextId = "item.romestead.new-items.ember_sword.name";
    private const string EmberSwordDescriptionTextId = "item.romestead.new-items.ember_sword.description";
    private const string EmberHelmetId = "armor:mod:ember_helmet";
    private const string EmberHelmetNameTextId = "item.romestead.new-items.ember_helmet.name";
    private const string EmberHelmetDescriptionTextId = "item.romestead.new-items.ember_helmet.description";
    private const string EmberTomeId = "weapon:mod:ember_tome";
    private const string EmberTomeNameTextId = "item.romestead.new-items.ember_tome.name";
    private const string EmberTomeDescriptionTextId = "item.romestead.new-items.ember_tome.description";

    private const string EmberWandId = "weapon:mod:ember_wand";
    private const string EmberWandNameTextId = "item.romestead.new-items.ember_wand.name";
    private const string EmberWandDescriptionTextId = "item.romestead.new-items.ember_wand.description";
    private const string EmberBoltSpellId = "item:scroll:bolt:3";
    private const string EmberShieldSpellId = "item:scroll:shield:3";

    internal const string ManaStatId = "Mana";
    internal const string ManaRegenStatId = "ManaRegeneration";
    private string _modDirectory = "";

    public void Initialize(IModContext context)
    {
        _modDirectory = context.ModDirectory;
        context.Ui.RegisterSettingsPage(new ModSettingsPageDefinition
        {
            Id = SettingsPageId,
            Title = "Emberwright",
            Icon = "scroll:red",
            Order = 100,
            Build = _ => BuildSettingsPage()
        });
        context.Ui.RegisterSidebarEntry(new ModSidebarEntryDefinition
        {
            Id = SidebarEntryId,
            Title = "Emberwright",
            Icon = "scroll:red",
            Order = 100,
            TargetPageId = SettingsPageId
        });

        context.Logger.Info("New Items Mod initialized.");
    }

    private static ModSettingsPage BuildSettingsPage()
    {
        return new ModSettingsPage
        {
            Sections =
            [
                new ModSection
                {
                    Title = "Overview",
                    Rows =
                    [
                        new ModLabelRow
                        {
                            Text = "Reference page for the Emberwright sample content and mana gear added by New Items Mod.",
                            Style = ModUiTextStyle.Body
                        },
                        new ModInfoRow
                        {
                            Label = "Skill",
                            Value = "Embercraft"
                        },
                        new ModInfoRow
                        {
                            Label = "Player class",
                            Value = "Emberwright"
                        }
                    ]
                },
                new ModSection
                {
                    Title = "Mana Gear",
                    Rows =
                    [
                        new ModInfoRow
                        {
                            Label = "Ember Wand mana cost",
                            Value = "20 basic / 35 charged"
                        },
                        new ModInfoRow
                        {
                            Label = "Ember Tome energy cost",
                            Value = "30 basic / 40 charged"
                        },
                        new ModInfoRow
                        {
                            Label = "Primary spell",
                            Value = EmberBoltSpellId
                        },
                        new ModInfoRow
                        {
                            Label = "Charged spell",
                            Value = EmberShieldSpellId
                        }
                    ]
                },
                new ModSection
                {
                    Title = "Registered Content IDs",
                    Rows =
                    [
                        new ModListRow
                        {
                            Label = "Items",
                            Values =
                            [
                                ItemId,
                                EmberSwordId,
                                EmberHelmetId,
                                EmberTomeId,
                                EmberWandId
                            ]
                        },
                        new ModListRow
                        {
                            Label = "Stats",
                            Values =
                            [
                                ManaStatId,
                                ManaRegenStatId
                            ]
                        }
                    ]
                }
            ]
        };
    }

    public void RegisterContent(IContentRegistry registry)
    {
        registry.Stats.Register(new StatDefinition
        {
            Id = ManaStatId,
            Name = "Mana",
            Description = "Your maximum magical reserves.",
            Icon = "ui:energy",
            Type = ModStatType.Entity,
            Flags = ModStatFlags.All,
            StringFormat = "0.",
            MinValue = 0f,
            MaxValue = 999999f,
            DefaultValue = 100f
        });

        registry.Stats.Register(new StatDefinition
        {
            Id = ManaRegenStatId,
            Name = "Mana Regeneration",
            Description = "Mana regeneration per second.",
            Icon = "ui:energy_regeneration",
            Type = ModStatType.Entity,
            Flags = ModStatFlags.All,
            StringFormat = "0.",
            MinValue = 0f,
            MaxValue = 999999f,
            DefaultValue = 2f
        });

        registry.Icons.Register(new IconDefinition
        {
            Id = ItemIconId,
            TexturePath = Path.Combine(_modDirectory, "assets", "icons", "icon_ember_resin.png"),
            SpriteWidth = 32,
            SpriteHeight = 32
        });

        // A dedicated bench so the ember recipes get their own crafting identity instead of
        // sharing the vanilla campfire. The header name resolves through the text registry.
        registry.CraftingStations.Register(new CraftingStationDefinition
        {
            Id = EmbercraftStationId,
            Name = "Embercraft Bench",
            IconId = ItemIconId
        });

        // Placeable embercraft bench: the player crafts and drops this in the world, then
        // presses E to open the embercraft crafting window. Phase 1 dumps the runtime
        // furniture/doodad structure so the clone wiring can target real keys.
        registry.Placeables.Register(new ModPlaceableStation
        {
            Id = "romestead.new-items.embercraft-bench",
            StationId = EmbercraftStationId,
            DisplayName = "Embercraft Bench",
            Description = "A compact ember-fed workbench for shaping resin, coal, and iron into Embercraft gear.",
            IconId = ItemIconId,
            TexturePath = Path.Combine(_modDirectory, "assets", "placeables", "embercraft_bench_sheet.png"),
            SpriteWidth = 32,
            SpriteHeight = 48,
            CollisionWidth = 28,
            CollisionHeight = 16,
            Template = VanillaBenchTemplate.WarTable
        });

        registry.Items.Register(new ItemDefinition
        {
            Id = ItemId,
            NameTextId = ItemNameTextId,
            Name = "Ember Resin",
            DescriptionTextId = ItemDescriptionTextId,
            Description = "A sticky volcanic resin prepared by the mod loader.",
            Icon = ItemIconId,
            MaxStackSize = 25,
            Tier = 1
        });

        registry.Recipes.Register(new RecipeDefinition
        {
            ResultItemId = ItemId,
            ResultAmount = 1,
            Station = EmbercraftStationId,
            Ingredients =
            [
                new RecipeIngredient("material:coal", 1),
                new RecipeIngredient("material:wood_stick", 2)
            ]
        });

        registry.Recipes.Register(new RecipeDefinition
        {
            ResultItemId = EmberSwordId,
            ResultAmount = 1,
            Station = EmbercraftStationId,
            Ingredients =
            [
                new RecipeIngredient(ItemId, 3),
                new RecipeIngredient("material:coal", 2)
            ]
        });

        registry.Items.Register(new ItemDefinition
        {
            Id = EmberSwordId,
            NameTextId = EmberSwordNameTextId,
            Name = "Ember Sword",
            DescriptionTextId = EmberSwordDescriptionTextId,
            Description = "A blade smelted in volcanic resin. Burns what it cuts.",
            Icon = "sword:iron",
            MaxStackSize = 1,
            Tier = 3,
            Equipment = new EquipmentDefinition
            {
                Slot = EquipmentSlot.Weapon,
                Material = EquipmentMaterial.Iron,
                Display = new EquipmentDisplayDefinition
                {
                    Id = EmberSwordDisplayId,
                    Fragments =
                    [
                        new EquipmentDisplayFragmentDefinition
                        {
                            SkinName = EmberSwordSkinName,
                            TexturePath = Path.Combine(_modDirectory, "assets", "equipment", "ember_sword_held.png"),
                            SpriteWidth = 48,
                            SpriteHeight = 48,
                            SkinTag = EquipmentSkinTag.Tool,
                            SpacTag = EquipmentSpacTag.Tool,
                            Layer = 1f
                        }
                    ]
                },
                HeldVfx = new EquipmentHeldVfxDefinition
                {
                    ParticleEmitterId = "flame_small",
                    ParticleOffsetX = 2f,
                    ParticleOffsetY = -2f,
                    ParticleOffsetZ = 14f,
                    ParticleLineLength = 26f,
                    ParticleLineWidth = 1.5f,
                    ParticleLineHeight = 4f,
                    ParticleLineAngleDegrees = -18f,
                    ParticleSpawnFrequency = 0.025f,
                    ParticleAmountSpawned = 1,
                    LightOffsetX = 10f,
                    LightOffsetY = -1f,
                    LightOffsetZ = 18f,
                    LightRadius = 58f,
                    LightIntensity = 1.45f,
                    LightFlickerAmount = 0.25f
                },
                Weapon = new WeaponStatsDefinition
                {
                    Class = WeaponClassPreset.Sword,
                    Damage =
                    [
                        new DamageRange { Type = DamageTypeId.Slashing, Min = 14, Max = 16 },
                        new DamageRange { Type = DamageTypeId.Pyro,     Min =  3, Max =  6 }
                    ],
                    SwingTimer = 0.45f,
                    BaseAttackRange = 26f,
                    BaseKnockback = 60f,
                    StunPower = 0.25f
                }
            }
        });

        registry.Items.Register(new ItemDefinition
        {
            Id = EmberTomeId,
            NameTextId = EmberTomeNameTextId,
            Name = "Ember Tome",
            DescriptionTextId = EmberTomeDescriptionTextId,
            Description = "A bound scroll that burns with the same fire that smelts the resin. Endless casts at the cost of breath.",
            Icon = "scroll:red",
            MaxStackSize = 1,
            Tier = 3,
            Equipment = new EquipmentDefinition
            {
                Slot = EquipmentSlot.Offhand,
                Material = EquipmentMaterial.Iron,
                Weapon = new WeaponStatsDefinition
                {
                    Class = WeaponClassPreset.SpellTome,
                    SwingTimer = 0.5f,
                    EnergyCost = 30f,
                    SpecialEnergyCost = 40f,
                    MovementFactor = 0.5f,
                    SpellTome = new SpellTomeDefinition
                    {
                        SpellId = EmberBoltSpellId,
                        ChargedSpellId = EmberShieldSpellId,
                        ChargeTime = 1f,
                        Target = SpellTarget.Self,
                        ChargedTarget = SpellTarget.Self
                    }
                }
            }
        });

        registry.Items.Register(new ItemDefinition
        {
            Id = EmberWandId,
            NameTextId = EmberWandNameTextId,
            Name = "Ember Wand",
            DescriptionTextId = EmberWandDescriptionTextId,
            Description = "A focus carved from Ember Resin. Channels pure mana into fire.",
            Icon = "scroll:red",
            MaxStackSize = 1,
            Tier = 3,
            Equipment = new EquipmentDefinition
            {
                Slot = EquipmentSlot.Weapon,
                Material = EquipmentMaterial.Iron,
                Weapon = new WeaponStatsDefinition
                {
                    Class = WeaponClassPreset.SpellTome,
                    SwingTimer = 0.5f,
                    MovementFactor = 0.7f,
                    ManaCost = 20f,
                    SpecialManaCost = 35f,
                    SpellTome = new SpellTomeDefinition
                    {
                        SpellId = EmberBoltSpellId,
                        ChargedSpellId = EmberShieldSpellId,
                        ChargeTime = 1f,
                        Target = SpellTarget.Self,
                        ChargedTarget = SpellTarget.Self
                    }
                }
            }
        });

        registry.Items.Register(new ItemDefinition
        {
            Id = EmberHelmetId,
            NameTextId = EmberHelmetNameTextId,
            Name = "Ember Helmet",
            DescriptionTextId = EmberHelmetDescriptionTextId,
            Description = "An iron helmet lined with ember-resin. Warm even in winter.",
            Icon = "helmet:iron",
            MaxStackSize = 1,
            Tier = 3,
            Equipment = new EquipmentDefinition
            {
                Slot = EquipmentSlot.Helmet,
                Material = EquipmentMaterial.Iron,
                DisplayId = "cdd:head:iron",
                StatBonuses =
                [
                    new StatBonusDefinition { StatId = "Armor", Additive = 14f },
                    new StatBonusDefinition { StatId = "Health", Additive = 20f },
                    new StatBonusDefinition { StatId = "EnergyRegeneration", Additive = 0.5f }
                ]
            }
        });

        registry.Skills.Register(new SkillDefinition
        {
            Id = SkillId,
            NameTextId = SkillNameTextId,
            Name = "Embercraft",
            DescriptionTextId = SkillDescriptionTextId,
            Description = "Improves volatile crafting techniques and resin refinement by {0}.",
            Icon = "trowel",
            Value = 0.05f,
            ExperienceGainFactor = 1.0f
        });

        registry.SkillEffects.Register(new SkillEffectDefinition
        {
            SkillId = SkillId,
            Type = SkillEffectType.ExperienceGainMultiplier,
            TargetSkillId = "skill:construction",
            ValuePerLevel = 0.03f
        });

        registry.PlayerClasses.Register(new PlayerClassDefinition
        {
            Id = "player_class:mod:emberwright",
            NameTextId = PlayerClassNameTextId,
            Name = "Emberwright",
            BonusSkill = SkillId,
            StartingClothes =
            [
                EmberHelmetId,
                "armor:civilian:8",
                "armor:civilian:legs",
                EmberWandId,
                EmberTomeId
            ],
            StartingInventory =
            [
                new RecipeIngredient("placeable:workbench", 1),
                // The custom placeable embercraft bench: place it, then press E to
                // open the embercraft crafting window.
                new RecipeIngredient("romestead.new-items.embercraft-bench", 1),
                new RecipeIngredient(ItemId, 3),
                new RecipeIngredient("food:meat_small", 2),
                new RecipeIngredient(EmberSwordId, 1)
            ]
        });
    }
}

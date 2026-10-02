using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Cards;
using UltimateCustomRun;

internal static class Program
{
    private static void Main(string[] args)
    {
        var dataPath = Path.Combine(args.Length > 0 ? args[0]
            : @"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2", "data_sts2_windows_x86_64");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(dataPath, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var assembly = typeof(ModEntry).Assembly;
        Check(assembly.GetName().Name == "UltimateCustomRun" && typeof(ModEntry).Namespace == "UltimateCustomRun" &&
            ModEntry.HarmonyId == "vertopolka.UltimateCustomRun", "Assembly, namespace and Harmony identity are UltimateCustomRun");
        var initializer = typeof(ModEntry).GetCustomAttribute<ModInitializerAttribute>();
        Check(initializer?.initializerMethod == nameof(ModEntry.Initialize), "Game loader can find Initialize");

        // Real Harmony patching against the installed game, outside the Godot UI.
        var harmony = new Harmony(ModEntry.HarmonyId);
        try
        {
            ModEntry.ApplyPatches();
            Check(!ModelDb.Contains(typeof(NeowStarterChoice)),
                "Mod initializer leaves content registration to game startup");
            var target = typeof(Specialized).GetMethod(nameof(Specialized.GenerateNeowOption))!;
            var patched = Harmony.GetPatchInfo(target);
            Check(patched == null || !patched.Owners.Contains(ModEntry.HarmonyId),
                "Original Specialized is not patched");
            Check(NativeModifierCountPatch.Targets.All(target => Harmony.GetPatchInfo(
                AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)))?.Owners.Contains(ModEntry.HarmonyId) == true),
                "All six native modifier count state machines are patched");

            // Avoid starting a run or constructing native Godot objects.
            var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
            var specialized = (Specialized)RuntimeHelpers.GetUninitializedObject(typeof(Specialized));
            var vanilla = specialized.GenerateNeowOption(neow);
            Check(vanilla.Method.DeclaringType!.Assembly == typeof(Specialized).Assembly,
                "Uninitialized event retains vanilla callback");

            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow, player);
            var unchanged = specialized.GenerateNeowOption(neow);
            Check(unchanged.Method.DeclaringType!.Assembly == typeof(Specialized).Assembly,
                "Original Specialized retains vanilla random callback");
            var pickAny = (SpecializedPickAny)RuntimeHelpers.GetUninitializedObject(typeof(SpecializedPickAny));
            var draft = (SpecializedDraft)RuntimeHelpers.GetUninitializedObject(typeof(SpecializedDraft));
            Check(pickAny.GenerateNeowOption(neow)?.Method.DeclaringType?.Assembly == assembly &&
                draft.GenerateNeowOption(neow)?.Method.DeclaringType?.Assembly == assembly,
                "Both new modifiers provide their own Neow buttons");

            TestNeowModifier();
            TestGroups();
            TestSpecializedVariants();
            TestModifierValues();
            TestRadioChains();
            TestColorlessCards();
            TestAscensionModifiers();
            TestDailyIsolation();
        }
        finally
        {
            harmony.UnpatchAll(ModEntry.HarmonyId);
        }
        Console.WriteLine("PASS: managed integration smoke checks. UI/deck acquisition require an in-game playtest.");
    }

    private static void TestGroups()
    {
        Check(ModifierGroups.Sections.Select(s => s.Title).SequenceEqual(
            new[] { "Improved Start", "Run Parameters", "Modifiers", "Card Pool", "Ascension", "Negatives", "Disabled" }), "Group titles and order match the requested layout");
        var negatives = new HashSet<Type> { typeof(BigGameHunter), typeof(CursedRun), typeof(DeadlyEvents),
            typeof(Midas), typeof(Murderous), typeof(NightTerrors), typeof(Terminal) };
        foreach (var type in new[] { typeof(NeowStarterChoice), typeof(Specialized), typeof(SpecializedPickAny), typeof(SpecializedDraft), typeof(Draft),
            typeof(SealedDeck), typeof(Insanity), typeof(AllStar), typeof(AllStarDraft) })
            Check(ModifierGroups.Classify((ModifierModel)RuntimeHelpers.GetUninitializedObject(type), negatives) == ModifierGroup.ImprovedStart,
                type.Name + " belongs to Improved Start");
        Check(ModifierGroups.Classify(ModelDb.Modifier<CharacterCards>(), negatives) == ModifierGroup.CardPool,
            "Character card pools belong to Card Pool");
        foreach (var type in negatives)
            Check(ModifierGroups.Classify((ModifierModel)RuntimeHelpers.GetUninitializedObject(type), negatives) == ModifierGroup.Negatives,
                type.Name + " belongs to Negatives");
        Check(ModifierGroups.Classify(ModelDb.Modifier<Hoarder>(), negatives) == ModifierGroup.Modifiers &&
            ModifierGroups.Classify(ModelDb.Modifier<Vintage>(), negatives) == ModifierGroup.Modifiers &&
            ModifierGroups.Classify(ModelDb.Modifier<Flight>(), negatives) == ModifierGroup.Modifiers,
            "Other modifiers stay in Modifiers");
    }

    private static void TestNeowModifier()
    {
        ModelDb.Init([typeof(Draft), typeof(SealedDeck), typeof(Hoarder), typeof(Specialized),
            typeof(Insanity), typeof(AllStar), typeof(Flight), typeof(Vintage), typeof(CharacterCards), typeof(NeowStarterChoice),
            typeof(SpecializedPickAny), typeof(SpecializedDraft), typeof(AllStarDraft), typeof(ColorlessCards),
            typeof(Friendship), typeof(FriendshipDraft), typeof(RichLoot), typeof(CardSwarm), typeof(CustomRunParameters),
            typeof(MegaCrit.Sts2.Core.Models.Relics.DingyRug),
            typeof(BigGameHunter), typeof(CursedRun), typeof(DeadlyEvents), typeof(Midas), typeof(Murderous), typeof(NightTerrors), typeof(Terminal),
            typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad), typeof(MegaCrit.Sts2.Core.Models.Characters.Silent),
            typeof(MegaCrit.Sts2.Core.Models.Characters.Regent), typeof(MegaCrit.Sts2.Core.Models.Characters.Necrobinder), typeof(MegaCrit.Sts2.Core.Models.Characters.Defect),
            typeof(MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool), typeof(Neow),
            typeof(SwarmingElites), typeof(WearyTraveler), typeof(Poverty), typeof(TightBelt), typeof(UltimateCustomRun.AscendersBane),
            typeof(Inflation), typeof(Scarcity), typeof(ToughEnemies), typeof(DeadlyEnemies), typeof(DoubleBoss)]);
        Check(ModelDb.All.Count(model => model is NeowStarterChoice) == 1,
            "Game startup constructs Neow!! once without DuplicateModelException");
        // Fixture for model IDs without invoking the engine-dependent startup logger.
        var cacheType = typeof(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache);
        var ids = ModelDb.All.Select(m => ModelDb.GetId(m.GetType())).ToArray();
        var categories = (Dictionary<string, int>)AccessTools.Field(cacheType, "_categoryNameToNetIdMap").GetValue(null)!;
        foreach (var id in ids.Select(id => id.Category).Distinct()) categories[id] = categories.Count;
        var entries = (Dictionary<string, int>)AccessTools.Field(cacheType, "_entryNameToNetIdMap").GetValue(null)!;
        foreach (var id in ids.Select(id => id.Entry).Distinct()) entries[id] = entries.Count;
        entries[ModelDb.GetId(typeof(TestNeow)).Entry] = entries.Count;
        var categoryNames = (List<string>)AccessTools.Field(cacheType, "_netIdToCategoryNameMap").GetValue(null)!;
        categoryNames.Clear();
        categoryNames.AddRange(categories.OrderBy(pair => pair.Value).Select(pair => pair.Key));
        var entryNames = (List<string>)AccessTools.Field(cacheType, "_netIdToEntryNameMap").GetValue(null)!;
        entryNames.Clear();
        entryNames.AddRange(entries.OrderBy(pair => pair.Value).Select(pair => pair.Key));
        AccessTools.Property(cacheType, "CategoryIdBitSize").SetValue(null, (int)Math.Ceiling(Math.Log2(categoryNames.Count)));
        AccessTools.Property(cacheType, "EntryIdBitSize").SetValue(null, (int)Math.Ceiling(Math.Log2(entryNames.Count)));
        AccessTools.Field(cacheType, "_initialized").SetValue(null, true);
        foreach (var type in ModelDb.All.Select(model => model.GetType()).Distinct())
            AccessTools.Method(cacheType, "CachePropertiesForType").Invoke(null, [type, null, null]);
        AccessTools.Property(cacheType, "PropertyIdBitSize").SetValue(null,
            (int)Math.Ceiling(Math.Log2(MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.MaxPropertyId + 1)));
        ModelDb.InitIds();
        Check(ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers).Count(m => m is NeowStarterChoice) == 1,
            "Neow!! is registered once in the modifier list");
        var modifier = ModelDb.Modifier<NeowStarterChoice>();
        Check(ModifierModel.FromSerializable(modifier.ToMutable().ToSerializable()) is NeowStarterChoice,
            "New modifier round-trips through game save serialization");
        Check(NeowStarterChoice.DisplayTitle == "Neow!!" &&
            NeowStarterChoice.DisplayDescription == "Brings back starter Neow relic choice",
            "Requested title and description are exact");

        var test = new TestNeow();
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        IReadOnlyList<ModifierModel> modifiers = [modifier, ModelDb.Modifier<Specialized>()];
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, modifiers);
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(test, player);
        AccessTools.Method(typeof(AbstractModel), "InitId").Invoke(test, [ModelDb.GetId<Neow>()]);
        Check(NeowRelicChoice.NeedsRelicChoices(test), "Marker enables starter relic choices");
        Check(NeowRelicChoice.ModifierCount(2, test) == 2, "Initial modifier chain remains enabled");
        AccessTools.Method(typeof(EventModel), "SetEventFinished").Invoke(test,
            [new LocString("events", test.Id.Entry + ".pages.DONE.description")]);
        Check(test.OptionsShown?.Count == 3 && !test.IsFinished,
            "Modifier-chain completion shows three relic offers instead of ending event");
        Check(!NeowRelicChoice.NeedsRelicChoices(test), "Relic choice is restored only once");
        Check(ReferenceEquals(run.Modifiers, modifiers) && run.Modifiers.Count == 2,
            "Restoring relics does not remove or replace run modifiers");
        Check(NeowRelicChoice.ModifierCount(2, test) == 2, "Temporary vanilla-generation scope is cleared");

        var failing = new TestNeow();
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(failing, player);
        failing.FailGeneration = true;
        try { NeowRelicChoice.GenerateRelicChoices(failing); throw new Exception("Expected generation failure"); }
        catch (TargetInvocationException) { }
        Check(NeowRelicChoice.NeedsRelicChoices(failing) && NeowRelicChoice.ModifierCount(2, failing) == 2,
            "Failed generation resets restoration state and scope");
    }

    private static void TestSpecializedVariants()
    {
        var pickAny = ModelDb.Modifier<SpecializedPickAny>();
        var draft = ModelDb.Modifier<SpecializedDraft>();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        Check(custom.Count(m => m is SpecializedPickAny) == 1 &&
            custom.Count(m => m is SpecializedDraft) == 1,
            "Specialized variants appear once in the same mod's modifier list");
        Check(ModifierModel.FromSerializable(pickAny.ToMutable().ToSerializable()) is SpecializedPickAny &&
            ModifierModel.FromSerializable(draft.ToMutable().ToSerializable()) is SpecializedDraft,
            "Both Specialized variants round-trip through game save serialization");
        Check(SpecializedExclusivityPatch.ShouldUntick(pickAny, draft) &&
            SpecializedExclusivityPatch.ShouldUntick(pickAny, ModelDb.Modifier<Specialized>()) &&
            SpecializedExclusivityPatch.ShouldUntick(draft, ModelDb.Modifier<Specialized>()),
            "Three Specialized modes are mutually exclusive in custom runs");
        var allStarDraft = ModelDb.Modifier<AllStarDraft>();
        Check(custom.Count(m => m is AllStarDraft) == 1 &&
            ModifierModel.FromSerializable(allStarDraft.ToMutable().ToSerializable()) is AllStarDraft,
            "All Star draft is registered once and survives save serialization");
        var order = custom.Select(m => m.GetType()).ToList();
        Check(order[order.IndexOf(typeof(AllStar)) + 1] == typeof(AllStarDraft),
            "All Star draft appears next to original All Star");
        Check(order[order.IndexOf(typeof(Specialized)) + 1] == typeof(SpecializedDraft) &&
            order[order.IndexOf(typeof(Specialized)) + 2] == typeof(SpecializedPickAny),
            "Specialized ordering remains normal, draft, any");
        var colorlessPool = ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>();
        var colorlessOptions = AllStarDraft.Options(colorlessPool);
        Check(AllStarDraft.RewardCount == 5 && colorlessOptions.CardPools.Single() == colorlessPool &&
            colorlessOptions.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications),
            "Five All Star draft rewards stay in the Colorless pool");
        Check(SpecializedExclusivityPatch.ShouldUntick(allStarDraft, ModelDb.Modifier<AllStar>()),
            "All Star and All Star draft are mutually exclusive in custom runs");
        Check(SpecializedDraft.DisplayDescription == "Choose [blue]1[/blue] card reward. Add [blue]5[/blue] copies yo your starting deck.",
            "Specialized draft uses requested wording and blue numbers");
        Check(SpecializedDraft.DisplayTitle == "Specialized - Draft" &&
            SpecializedPickAny.DisplayTitle == "Specialized - Pick Any" && AllStarDraft.DisplayTitle == "All Star - Draft",
            "Only variant titles use the requested hyphen");
        const string rowTemplate = "[{color}]{modifier_title}[/{color}]: {modifier_description}";
        var uiTable = new LocTable("main_menu_ui", new Dictionary<string, string> { ["CUSTOM_RUN_SCREEN.MODIFIER_LABEL"] = rowTemplate });
        AccessTools.Method(typeof(ModifierTextPatch), "Postfix").Invoke(null, ["main_menu_ui", uiTable]);
        Check(uiTable.GetRawText("CUSTOM_RUN_SCREEN.MODIFIER_LABEL") == rowTemplate,
            "Native colon between modifier title and description remains unchanged");
        var readyPatches = Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NCustomRunModifiersList), "_Ready"));
        Check(readyPatches?.Transpilers.Any(p => p.owner == ModEntry.HarmonyId) == true,
            "Custom modifiers are injected at the actual UI list creation call site");
        Check(SpecializedDraftReward.OfferCount == 3 && SpecializedCardChoices.Copies == 5,
            "Draft offers three cards and acquisition grants five copies");
        var cardType = typeof(CardModel).Assembly.GetTypes().First(type => !type.IsAbstract && typeof(CardModel).IsAssignableFrom(type));
        var first = (CardModel)RuntimeHelpers.GetUninitializedObject(cardType);
        var second = (CardModel)RuntimeHelpers.GetUninitializedObject(cardType);
        var poolType = typeof(CardPoolModel).Assembly.GetTypes().First(type => !type.IsAbstract && typeof(CardPoolModel).IsAssignableFrom(type));
        var pool = (CardPoolModel)RuntimeHelpers.GetUninitializedObject(poolType);
        Check(SpecializedDraftReward.Options(pool).RarityOdds == CardRarityOddsType.Uniform,
            "Draft uses all-rarity uniform reward generation");
        var offers = new MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult[] { new(first), new(second) };
        Check(ReferenceEquals(SpecializedDraftReward.ResolveSelection(offers, 1), second),
            "Draft grants the selected reward rather than the first offer");
        Check(SpecializedDraftReward.ResolveSelection(offers, null) == null, "Skipped rewards do not grant cards");
        foreach (var index in new[] { -1, 2 })
        {
            try { SpecializedDraftReward.ResolveSelection(offers, index); throw new Exception("Expected invalid selection rejection"); }
            catch (InvalidOperationException) { }
        }
        Check(true, "Invalid reward indices are rejected");
    }

    private static void TestModifierValues()
    {
        var cases = new (ModifierModel Model, int Min, int Max, int Step, int Default)[]
        {
            (ModelDb.Modifier<Specialized>(), 1, 10, 1, 5),
            (ModelDb.Modifier<SpecializedDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<SpecializedPickAny>(), 1, 10, 1, 5),
            (ModelDb.Modifier<AllStar>(), 1, 10, 1, 5),
            (ModelDb.Modifier<AllStarDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<Friendship>(), 1, 10, 1, 5),
            (ModelDb.Modifier<FriendshipDraft>(), 1, 10, 1, 5),
            (ModelDb.Modifier<Draft>(), 5, 20, 5, 10),
            (ModelDb.Modifier<SealedDeck>(), 5, 20, 5, 10),
            (ModelDb.Modifier<Insanity>(), 5, 60, 5, 30),
            (ModelDb.Modifier<Hoarder>(), 1, 5, 1, 2),
            (ModelDb.Modifier<Midas>(), 150, 300, 5, 200)
        };
        foreach (var (canonical, min, max, step, defaultValue) in cases)
        {
            var spec = ModifierValues.For(canonical)!;
            Check((spec.Min, spec.Max, spec.Step, spec.Default) == (min, max, step, defaultValue),
                canonical.GetType().Name + " has the requested range, step and vanilla default");
            var model = canonical.ToMutable();
            Check(ModifierValues.Get(model) == defaultValue, "Unconfigured modifier preserves default: " + model.GetType().Name);
            for (var value = min; value <= max; value += step)
            {
                ModifierValues.Set(model, value);
                var serializable = model.ToSerializable();
                var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
                serializable.Props!.Serialize(writer);
                var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
                reader.Reset(writer.Buffer);
                var props = reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SavedProperties>();
                var fromNetwork = ModifierModel.FromSerializable(new MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier
                    { Id = serializable.Id, Props = props });
                Check(ModifierValues.Get(ModifierModel.FromSerializable(serializable)) == value &&
                    ModifierValues.Get(fromNetwork) == value &&
                    ModifierValues.Get((ModifierModel)model.MutableClone()) == value,
                    $"{model.GetType().Name} value {value} survives save, network properties and clone");
            }
            ModifierValues.Set(model, int.MinValue);
            Check(ModifierValues.Get(model) == min, "Lower bound enforced: " + model.GetType().Name);
            ModifierValues.Set(model, int.MaxValue);
            Check(ModifierValues.Get(model) == max, "Upper bound enforced: " + model.GetType().Name);
            if (step > 1)
            {
                ModifierValues.Set(model, min + 3);
                Check(ModifierValues.Get(model) == min + step, "Off-step values normalized: " + model.GetType().Name);
            }
            Check(ModifierValues.Get(canonical) == defaultValue, "Configuration cannot leak to canonical/daily model: " + model.GetType().Name);
        }
        var midas = ModelDb.Modifier<Midas>().ToMutable();
        ModifierValues.Set(midas, 155);
        Check(ModifierValues.ScaleGold(20, midas) == 31 && ModifierValues.ScaleGold(21, midas) == 32,
            "Midas uses the selected percent with integer gold rounding");
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
        var insanity = ModelDb.Modifier<Insanity>().ToMutable();
        ModifierValues.Set(insanity, 60);
        AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { insanity });
        AccessTools.Field(typeof(Player), "_runState").SetValue(player, run);
        Check(ModifierValues.ForPlayer<Insanity>(player) == 60, "Native gameplay resolves the run's selected count");
        var rewards = new List<MegaCrit.Sts2.Core.Rewards.Reward>
            { new MegaCrit.Sts2.Core.Rewards.GoldReward(20, player), new MegaCrit.Sts2.Core.Rewards.GoldReward(21, player) };
        ((Midas)midas).TryModifyRewardsLate(player, rewards, null);
        Check(rewards.Cast<MegaCrit.Sts2.Core.Rewards.GoldReward>().Select(reward => reward.Amount).SequenceEqual(new[] { 31, 32 }),
            "Patched native Midas reward method applies 155 percent instead of its fixed multiplier");
        foreach (var target in NativeModifierCountPatch.Targets)
        {
            var instructions = PatchProcessor.GetCurrentInstructions(AccessTools.AsyncMoveNext(AccessTools.Method(target.Type, target.Method)));
            Check(instructions.Count(instruction => instruction.opcode == System.Reflection.Emit.OpCodes.Call &&
                instruction.operand is MethodInfo method && method.DeclaringType == typeof(ModifierValues) &&
                method.Name is nameof(ModifierValues.ForPlayer) or nameof(ModifierValues.Get)) == 1,
                target.Type.Name + " gameplay replaces exactly one count with the configured resolver");
        }
    }

    private static void TestColorlessCards()
    {
        var modifier = ModelDb.Modifier<ColorlessCards>();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        Check(custom.Count(m => m is ColorlessCards) == 1 &&
            ModifierGroups.Classify(modifier, new HashSet<Type>()) == ModifierGroup.CardPool,
            "Colorless Cards appears once in Card Pool");
        Check(custom[0] is ColorlessCards, "Colorless relic is granted before starting card rewards");
        Check(ModifierModel.FromSerializable(modifier.ToMutable().ToSerializable()) is ColorlessCards,
            "Colorless Cards survives save serialization");
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        var relics = (List<RelicModel>)AccessTools.Field(typeof(Player), "_relics").GetValue(player)!;
        // Create the native collection for this engine-free fixture.
        if (relics == null)
        {
            relics = [];
            AccessTools.Field(typeof(Player), "_relics").SetValue(player, relics);
        }
        relics.Add(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DingyRug>());
        var neow = (Neow)RuntimeHelpers.GetUninitializedObject(typeof(Neow));
        typeof(EventModel).GetProperty(nameof(EventModel.Owner))!.SetValue(neow, player);
        modifier.GenerateNeowOption(neow)!().GetAwaiter().GetResult();
        Check(relics.Count == 1, "Existing Dingy Rug does not get granted twice");
        Check(typeof(ColorlessCards).GetMethod(nameof(ModifierModel.ModifyCardRewardCreationOptions))!.DeclaringType != typeof(ColorlessCards),
            "Colorless card rewards are handled by the native relic, not a duplicate modifier hook");
        var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(RelicModel), nameof(RelicModel.IsAllowed)));
        Check(patches == null || !patches.Owners.Contains(ModEntry.HarmonyId), "Relic availability is not patched");
        Check(!ColorlessCards.DisplayDescription.Contains("Dingy", StringComparison.OrdinalIgnoreCase),
            "Public description only describes colorless rewards");
    }

    private static void TestRadioChains()
    {
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        var start = custom.Where(modifier => ModifierGroups.Classify(modifier, new HashSet<Type>()) == ModifierGroup.ImprovedStart).ToArray();
        Check(start.Take(3).Select(modifier => modifier.GetType()).SequenceEqual(new[] { typeof(Draft), typeof(SealedDeck), typeof(Insanity) }),
            "Draft, Sealed Deck and Insanity are adjacent for their shared chain");
        var linked = Enumerable.Range(1, start.Length - 1).Where(index => LinkedModifierChains.AreLinked(start[index - 1], start[index])).ToArray();
        Check(linked.Length == 6, "Only the six adjacent mutually exclusive pairs receive chains, including Friendship");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<Draft>(), ModelDb.Modifier<SealedDeck>()) &&
            LinkedModifierChains.AreLinked(ModelDb.Modifier<SealedDeck>(), ModelDb.Modifier<Insanity>()), "Native deck replacement choices are linked");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<Specialized>(), ModelDb.Modifier<SpecializedDraft>()) &&
            LinkedModifierChains.AreLinked(ModelDb.Modifier<SpecializedDraft>(), ModelDb.Modifier<SpecializedPickAny>()), "Specialized choices are linked");
        Check(LinkedModifierChains.AreLinked(ModelDb.Modifier<AllStar>(), ModelDb.Modifier<AllStarDraft>()), "All Star choices are linked");
        Check(!LinkedModifierChains.AreLinked(ModelDb.Modifier<Insanity>(), ModelDb.Modifier<Specialized>()) &&
            !LinkedModifierChains.AreLinked(ModelDb.Modifier<AllStarDraft>(), ModelDb.Modifier<NeowStarterChoice>()), "Independent modifiers are not visually linked");
    }

    private static void TestAscensionModifiers()
    {
        var levels = Enum.GetValues<MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel>().Where(level => (int)level > 0).ToArray();
        var custom = ModifierListPatch.ForCustomRun(ModelDb.GoodModifiers);
        var effects = custom.OfType<AscensionModifier>().ToArray();
        Check(effects.Select(effect => effect.Level).SequenceEqual(levels), "All ten ascension effects appear once in level order");
        Check(ModifierListPatch.ForCustomRun(custom).OfType<AscensionModifier>().Count() == levels.Length,
            "Repeated custom enumeration does not duplicate ascension modifiers");
        foreach (var effect in effects)
        {
            Check(ModifierGroups.Classify(effect, new HashSet<Type>()) == ModifierGroup.Ascension && ModifierListPatch.IsCustomOnly(effect),
                effect.GetType().Name + " is custom-only and belongs to Ascension");
            var saved = effect.ToSerializable();
            var writer = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketWriter();
            saved.Serialize(writer);
            var reader = new MegaCrit.Sts2.Core.Multiplayer.Serialization.PacketReader();
            reader.Reset(writer.Buffer);
            var network = reader.Read<MegaCrit.Sts2.Core.Saves.Runs.SerializableModifier>();
            var restored = ModifierModel.FromSerializable(network);
            Check(restored is AscensionModifier ascension && ascension.Level == effect.Level &&
                ModifierModel.FromSerializable(saved).GetType() == effect.GetType(),
                effect.GetType().Name + " survives save and network serialization");
            var run = (RunState)RuntimeHelpers.GetUninitializedObject(typeof(RunState));
            AccessTools.Field(typeof(RunState), "<Modifiers>k__BackingField").SetValue(run, new ModifierModel[] { restored });
            var manager = new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(0);
            IndependentAscensionPatch.Bind(manager, run);
            foreach (var level in levels)
                Check(manager.HasLevel(level) == (level == effect.Level), effect.GetType().Name + " activates only " + level);
        }
        var combined = new ModifierModel[] { ModelDb.Modifier<DoubleBoss>().ToMutable(), ModelDb.Modifier<TightBelt>().ToMutable(), ModelDb.Modifier<RichLoot>().ToMutable() };
        Check(AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.DoubleBoss) &&
            AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.TightBelt) &&
            !AscensionModifiers.HasLevel(combined, MegaCrit.Sts2.Core.Entities.Ascension.AscensionLevel.Poverty), "Selected ascension effects combine without enabling intervening levels");
        Check(AscensionModifiers.WithoutAscensions(combined).Single() == combined[2], "Changing ascension removes all independent effects and preserves other modifiers");
        var vanilla = new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(10);
        Check(levels.All(vanilla.HasLevel), "Unbound vanilla Ascension 10 still enables every level");
        foreach (var target in new[] { "OnModifiersListChanged", "AscensionChanged" })
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen), target))?.Owners.Contains(ModEntry.HarmonyId) == true,
                "Ascension/modifier selection synchronization is patched: " + target);
    }

    private static void TestDailyIsolation()
    {
        foreach (var property in new[] { nameof(ModelDb.GoodModifiers), nameof(ModelDb.BadModifiers), nameof(ModelDb.MutuallyExclusiveModifiers) })
        {
            var info = Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(ModelDb), property));
            Check(info == null || !info.Owners.Contains(ModEntry.HarmonyId), "Daily global pool is not patched: " + property);
        }
        Check(!ModelDb.GoodModifiers.Any(ModifierListPatch.IsCustomOnly), "Daily cannot roll any of our custom-only modifiers");
        Check(ModelDb.GoodModifiers.Select(m => m.GetType()).SequenceEqual(new[]
            { typeof(Draft), typeof(SealedDeck), typeof(Hoarder), typeof(Specialized), typeof(Insanity), typeof(AllStar), typeof(Flight), typeof(Vintage), typeof(CharacterCards) }),
            "Vanilla good-modifier count and order are preserved");
        string Signature(ulong seed) => string.Join("|", ModifierModel.Pick2Good1Bad(new MegaCrit.Sts2.Core.Random.Rng(seed), [])
            .Select(m => m is CharacterCards cards ? m.Id + "/" + cards.CharacterModel : m.Id.ToString()));
        var withMod = Enumerable.Range(0, 100).Select(seed => Signature((ulong)seed)).ToArray();
        var harmony = new Harmony(ModEntry.HarmonyId);
        harmony.UnpatchAll(ModEntry.HarmonyId);
        try
        {
            var vanilla = Enumerable.Range(0, 100).Select(seed => Signature((ulong)seed)).ToArray();
            Check(withMod.SequenceEqual(vanilla), "100 daily seeds select exactly the same modifiers with and without our patches");
        }
        finally { ModEntry.ApplyPatches(); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS: " + message);
    }
}

internal class TestNeow : Neow
{
    public bool FailGeneration;
    public IReadOnlyList<EventOption>? OptionsShown;
    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        if (FailGeneration) throw new InvalidOperationException("Test generation failure");
        if (NeowRelicChoice.ModifierCount(2, this) != 0) throw new Exception("Vanilla path was not enabled");
        return Enumerable.Range(0, 3).Select(_ =>
            (EventOption)RuntimeHelpers.GetUninitializedObject(typeof(EventOption))).ToArray();
    }
    protected override void SetEventState(LocString description, IEnumerable<EventOption> options) =>
        OptionsShown = options.ToArray();
}

using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Valheim1CompatBridges
{
    /// <summary>
    /// Preloader BepInEx dla Valheim 1.0: mostki ze starych sygnatur API na nowe, zeby mody zbudowane
    /// pod wczesniejsze wersje gry (Weedheim, WeedheimShip, Marketplace And Server NPCs, stare mody
    /// Azumatta...) nie padaly z MissingMethodException / MissingFieldException / "Undefined target method".
    /// Nie modyfikuje zadnego moda - dodaje do assembly_valheim.dll brakujace przeciazenia, ktore wolaja
    /// nowe metody z wartosciami domyslnymi.
    /// </summary>
    public static class Patcher
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("Valheim1CompatBridges");
        private static int _added;

        public static IEnumerable<string> TargetDLLs { get; } = new[] { "assembly_valheim.dll" };

        /// <summary>Literal do wstawienia za brakujacy parametr (int/short/long/bool).</summary>
        private sealed class Lit { public readonly long V; public Lit(long v) { V = v; } }
        private static Lit L(long v) => new Lit(v);
        private const bool F = false, T = true;
        private static readonly object D = null;   // default: 0 / false / null / default(struct)

        public static void Patch(AssemblyDefinition assembly)
        {
            ModuleDefinition m = assembly.MainModule;
            _added = 0;

            BridgeInventoryChanged(m);
            ConstToStaticField(m, "ZRoutedRpc", "Everybody");
            InjectInventoryGridElement(m);
            InjectMethodStub(m, "Minimap", "OnMapRightClick");            // 1.0 nie ma prawego klikniecia na mapie - stub, zeby patche Harmony mialy cel
            InjectAwakeCalledFromOnEnable(m, "InventoryGrid");
            // interfejsy, ktore w 1.0 dostaly nowe metody: klasa moda bez nich pada na "VTable setup failed" - dajemy domyslna implementacje
            DefaultInterfaceMethod(m, "Hoverable", "GetHoverOffset");

            // --- zwykle forwardery: stare przeciazenie -> nowa metoda ---------------------------------------
            // spec: int = indeks starego parametru, bool/Lit = literal, D = wartosc domyslna typu
            Fwd(m, "Character", "Message", A("MessageHud/MessageType", "System.String", "System.Int32", "UnityEngine.Sprite"), S(0, 1, 2, 3, F));
            Fwd(m, "MessageHud", "ShowMessage", A("MessageHud/MessageType", "System.String", "System.Int32", "UnityEngine.Sprite", "System.Boolean"), S(0, 1, 2, 3, 4, T));
            Fwd(m, "EffectList", "Create", A("UnityEngine.Vector3", "UnityEngine.Quaternion", "UnityEngine.Transform", "System.Single", "System.Int32"), S(0, 1, 2, 3, 4, D));
            Fwd(m, "SEMan", "AddStatusEffect", A("System.Int32", "System.Boolean", "System.Int32", "System.Single"), S(0, 1, 2, 3, L(-1)));
            Fwd(m, "SEMan", "AddStatusEffect", A("StatusEffect", "System.Boolean", "System.Int32", "System.Single"), S(0, 1, 2, 3, L(-1)));
            Fwd(m, "ItemDrop/ItemData", "GetTooltip", A("ItemDrop/ItemData", "System.Int32", "System.Boolean", "System.Single", "System.Int32"), S(0, 1, 2, 3, 4, F));
            Fwd(m, "Inventory", "IsTeleportable", A(), S(F));
            Fwd(m, "Humanoid", "IsTeleportable", A(), S(F));
            Fwd(m, "Inventory", "AddItem", A("System.String", "System.Int32", "System.Single", "Vector2i", "System.Boolean", "System.Int32", "System.Int32", "System.Int64", "System.String", "System.Collections.Generic.Dictionary`2<System.String,System.String>", "System.Int32", "System.Boolean"), S(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, F, F));
            // AddItem(ItemData, amount, x, y) dostalo piaty parametr skipValidPositionCheck (domyslnie false)
            Fwd(m, "Inventory", "AddItem", A("ItemDrop/ItemData", "System.Int32", "System.Int32", "System.Int32"), S(0, 1, 2, 3, F));
            Fwd(m, "Terminal/ConsoleCommand", ".ctor", A("System.String", "System.String", "Terminal/ConsoleEvent", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean", "System.Boolean", "Terminal/ConsoleOptionsFetcher", "System.Boolean", "System.Boolean", "System.Boolean"), S(0, 1, 2, 3, 4, 5, 6, 7, F, 8, 9, 10, 11));
            Fwd(m, "Terminal/ConsoleEventArgs", ".ctor", A("System.String", "Terminal"), S(0, 1, D));
            Fwd(m, "YesNoPopup", ".ctor", A("System.String", "System.String", "PopupButtonCallback", "PopupButtonCallback", "System.Boolean"), S(0, 1, 2, 3, 4, F));
            Fwd(m, "Heightmap", "Poke", A("System.Boolean"), S(D, 0));
            Fwd(m, "TerrainComp", "Save", A(), S(F));
            Fwd(m, "Piece", "SetCreator", A("System.Int64"), S(0, D));
            // VisEquipment: nazwa prefabu (string) -> hash (int); quality = 0
            Fwd(m, "VisEquipment", "SetChestItem", A("System.String"), S(0));
            Fwd(m, "VisEquipment", "SetLegItem", A("System.String"), S(0));
            Fwd(m, "VisEquipment", "SetHelmetItem", A("System.String"), S(0));
            Fwd(m, "VisEquipment", "SetRightItem", A("System.String"), S(0, D));
            Fwd(m, "VisEquipment", "SetRightBackItem", A("System.String"), S(0, D));
            Fwd(m, "VisEquipment", "SetLeftItem", A("System.String", "System.Int32"), S(0, 1, D));
            Fwd(m, "VisEquipment", "SetLeftBackItem", A("System.String", "System.Int32"), S(0, 1, D));
            Fwd(m, "VisEquipment", "SetShoulderItem", A("System.String", "System.Int32"), S(0, 1, D));
            Fwd(m, "VisEquipment", "SetUtilityItem", A("System.String"), S(0));
            // AttachArmor dostalo trzeci parametr quality (domyslnie 0) - D daje wlasnie 0
            Fwd(m, "VisEquipment", "AttachArmor", A("System.Int32", "System.Int32"), S(0, 1, D));

            Log.LogInfo($"Gotowe: {_added} mostkow/wstrzykniec w assembly_valheim.dll.");
        }

        private static string[] A(params string[] types) => types;
        private static object[] S(params object[] spec) => spec;

        // ------------------------------------------------------------------------------------------------
        /// <summary>
        /// Inventory.Changed() - mody wolaja je bez parametrow, a w 1.0 jest tylko prywatne Changed(bool, bool).
        /// Oryginal dostaje nazwe ChangedEx (AzuAutoStore patchuje "Changed" po samej nazwie - dwa przeciazenia
        /// = AmbiguousMatchException), most Changed() => ChangedEx(false, false) zostaje jedyna metoda o tej nazwie,
        /// a wywolania Changed(false, false) w grze sa przepinane na most, zeby postfixy modow odpalaly sie
        /// przy kazdej zmianie ekwipunku.
        /// </summary>
        private static void BridgeInventoryChanged(ModuleDefinition module)
        {
            TypeDefinition inventory = module.GetType("Inventory");
            if (inventory == null) { Log.LogWarning("Brak typu Inventory."); return; }
            if (inventory.Methods.Any(x => x.Name == "Changed" && !x.HasParameters)) { Log.LogInfo("Inventory.Changed() juz istnieje."); return; }

            MethodDefinition target = inventory.Methods.FirstOrDefault(x => x.Name == "Changed" && x.Parameters.Count == 2 &&
                x.Parameters.All(p => p.ParameterType.MetadataType == MetadataType.Boolean));
            if (target == null) { Log.LogWarning("Inventory nie ma Changed(bool, bool) - most pominiety."); return; }

            target.Name = "ChangedEx";
            MethodDefinition bridge = new MethodDefinition("Changed", MethodAttributes.Public | MethodAttributes.HideBySig, module.ImportReference(typeof(void)));
            ILProcessor il = bridge.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldarg_0)); il.Append(il.Create(OpCodes.Ldc_I4_0)); il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Call, target)); il.Append(il.Create(OpCodes.Ret));
            inventory.Methods.Add(bridge);

            int rerouted = 0;
            foreach (TypeDefinition type in module.GetTypes())
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody || method == bridge) continue;
                    foreach (Instruction ins in method.Body.Instructions.ToList())
                    {
                        if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) || ins.Operand != target) continue;
                        Instruction a = ins.Previous, b = a?.Previous;
                        if (a == null || b == null || a.OpCode != OpCodes.Ldc_I4_0 || b.OpCode != OpCodes.Ldc_I4_0) continue;
                        a.OpCode = OpCodes.Nop; b.OpCode = OpCodes.Nop;
                        ins.OpCode = OpCodes.Call; ins.Operand = bridge;
                        rerouted++;
                    }
                }
            _added++;
            Log.LogInfo($"Inventory.Changed() -> ChangedEx(false, false); przepieto {rerouted} wywolan gry.");
        }

        /// <summary>const -> statyczne pole inicjowane w .cctor. Stare mody czytaja je jako pole (ldsfld) - z const to MissingFieldException.</summary>
        private static void ConstToStaticField(ModuleDefinition module, string typeName, string fieldName)
        {
            TypeDefinition type = module.GetType(typeName);
            FieldDefinition f = type?.Fields.FirstOrDefault(x => x.Name == fieldName);
            if (f == null) { Log.LogWarning($"Brak pola {typeName}.{fieldName}."); return; }
            if (!f.HasConstant) { Log.LogInfo($"{typeName}.{fieldName} nie jest const - nic do roboty."); return; }

            object value = f.Constant;
            f.HasConstant = false; f.Constant = null;
            f.Attributes = (f.Attributes & ~(FieldAttributes.Literal | FieldAttributes.HasDefault)) | FieldAttributes.Static;

            MethodDefinition cctor = type.Methods.FirstOrDefault(x => x.Name == ".cctor");
            if (cctor == null)
            {
                cctor = new MethodDefinition(".cctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.ImportReference(typeof(void)));
                cctor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
                type.Methods.Add(cctor);
            }
            ILProcessor il = cctor.Body.GetILProcessor();
            Instruction first = cctor.Body.Instructions[0];
            Instruction load = value is long l ? il.Create(OpCodes.Ldc_I8, l) : il.Create(OpCodes.Ldc_I4, Convert.ToInt32(value));
            il.InsertBefore(first, load);
            il.InsertBefore(first, il.Create(OpCodes.Stsfld, f));
            _added++;
            Log.LogInfo($"{typeName}.{fieldName}: const -> statyczne pole (= {value}).");
        }

        /// <summary>
        /// InventoryGrid.Element (stara zagniezdzona klasa) - w 1.0 to InventoryElement (komponent na prefabie, wiec nie da sie
        /// podstawic podklasy). Dodajemy Element jako lekki wrapper z polami starej klasy (m_go, m_pos, m_icon, ...), a GetElement /
        /// GetHoveredElement dostaja przeciazenia zwracajace Element (CLR pozwala roznic sie samym typem zwracanym) - stare mody
        /// trafiaja w swoja sygnature i czytaja m_go/m_pos. Do tego pusty List&lt;Element&gt; m_elements_compat: stare petle
        /// po nim nic nie robia zamiast rzucac MissingFieldException.
        /// ponytail: shadow NIE moze nazywac sie m_elements - dwa pola o tej samej nazwie sa legalne w IL (ref zawiera typ),
        /// ale AccessTools.Field(type, "m_elements") rzuca wtedy AmbiguousMatchException i psuje kazdy mod wstrzykujacy
        /// ___m_elements przez refleksje (AzuEPI, AzuAutoStore). Mody czytajace shadow po IL trzeba przekierowac na nowa
        /// nazwe (patrz tools/fix-marketplace-melements.ps1). Jesli kiedys zniknie ostatni taki mod - wywalic caly shadow.
        /// </summary>
        private static void InjectInventoryGridElement(ModuleDefinition module)
        {
            TypeDefinition grid = module.GetType("InventoryGrid");
            TypeDefinition src = module.GetType("InventoryElement");
            if (grid == null || src == null) { Log.LogWarning("Brak InventoryGrid/InventoryElement."); return; }
            if (grid.NestedTypes.Any(x => x.Name == "Element")) { Log.LogInfo("InventoryGrid.Element juz istnieje."); return; }

            TypeDefinition el = new TypeDefinition(null, "Element", TypeAttributes.NestedPublic | TypeAttributes.Class | TypeAttributes.BeforeFieldInit, module.TypeSystem.Object);
            MethodReference objCtor = module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes));
            MethodDefinition ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.ImportReference(typeof(void)));
            ILProcessor cil = ctor.Body.GetILProcessor();
            cil.Append(cil.Create(OpCodes.Ldarg_0)); cil.Append(cil.Create(OpCodes.Call, objCtor)); cil.Append(cil.Create(OpCodes.Ret));
            el.Methods.Add(ctor);

            // pola lustrzane (te same nazwy i typy co w InventoryElement) + m_go i m_pos ze starej klasy
            List<FieldDefinition> mirrored = new List<FieldDefinition>();
            foreach (FieldDefinition f in src.Fields.Where(x => !x.IsStatic && x.Name.StartsWith("m_")))
            {
                FieldDefinition nf = new FieldDefinition(f.Name, FieldAttributes.Public, f.FieldType);
                el.Fields.Add(nf); mirrored.Add(nf);
            }
            MethodDefinition getRect = src.Methods.First(x => x.Name == "GetElementRectTransform");
            MethodDefinition getPos = src.Methods.First(x => x.Name == "get_Position");
            AssemblyDefinition core = module.AssemblyResolver.Resolve(new AssemblyNameReference("UnityEngine.CoreModule", null));
            TypeDefinition componentT = core.MainModule.GetType("UnityEngine.Component");
            MethodReference getGo = module.ImportReference(componentT.Methods.First(x => x.Name == "get_gameObject"));
            FieldDefinition mGo = new FieldDefinition("m_go", FieldAttributes.Public, module.ImportReference(core.MainModule.GetType("UnityEngine.GameObject")));
            FieldDefinition mPos = new FieldDefinition("m_pos", FieldAttributes.Public, getPos.ReturnType);
            el.Fields.Add(mGo); el.Fields.Add(mPos);
            grid.NestedTypes.Add(el);

            // Element Wrap(InventoryElement e) - statyczna fabryka wrappera
            MethodDefinition wrap = new MethodDefinition("Wrap", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, el);
            wrap.Parameters.Add(new ParameterDefinition("e", ParameterAttributes.None, src));
            ILProcessor wil = wrap.Body.GetILProcessor();
            Instruction retNull = wil.Create(OpCodes.Ldnull);
            wil.Append(wil.Create(OpCodes.Ldarg_0)); wil.Append(wil.Create(OpCodes.Brfalse, retNull));
            wil.Append(wil.Create(OpCodes.Newobj, ctor));
            foreach (FieldDefinition nf in mirrored)
            {
                FieldDefinition sf = src.Fields.First(x => x.Name == nf.Name);
                wil.Append(wil.Create(OpCodes.Dup)); wil.Append(wil.Create(OpCodes.Ldarg_0)); wil.Append(wil.Create(OpCodes.Ldfld, sf)); wil.Append(wil.Create(OpCodes.Stfld, nf));
            }
            wil.Append(wil.Create(OpCodes.Dup)); wil.Append(wil.Create(OpCodes.Ldarg_0)); wil.Append(wil.Create(OpCodes.Callvirt, getRect)); wil.Append(wil.Create(OpCodes.Callvirt, getGo)); wil.Append(wil.Create(OpCodes.Stfld, mGo));
            wil.Append(wil.Create(OpCodes.Dup)); wil.Append(wil.Create(OpCodes.Ldarg_0)); wil.Append(wil.Create(OpCodes.Callvirt, getPos)); wil.Append(wil.Create(OpCodes.Stfld, mPos));
            wil.Append(wil.Create(OpCodes.Ret));
            wil.Append(retNull); wil.Append(wil.Create(OpCodes.Ret));
            el.Methods.Add(wrap);

            foreach (string name in new[] { "GetElement", "GetHoveredElement" })
            {
                MethodDefinition orig = grid.Methods.FirstOrDefault(x => x.Name == name && x.ReturnType == src);
                if (orig == null) continue;
                MethodDefinition over = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.HideBySig, el);
                foreach (ParameterDefinition p in orig.Parameters) over.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
                ILProcessor il = over.Body.GetILProcessor();
                il.Append(il.Create(OpCodes.Ldarg_0));
                for (int i = 0; i < over.Parameters.Count; i++) il.Append(il.Create(OpCodes.Ldarg, over.Parameters[i]));
                il.Append(il.Create(OpCodes.Call, orig));
                il.Append(il.Create(OpCodes.Call, wrap));
                il.Append(il.Create(OpCodes.Ret));
                grid.Methods.Add(over);
            }

            // pusty List<Element> m_elements obok prawdziwego List<InventoryElement> m_elements
            FieldDefinition realList = grid.Fields.FirstOrDefault(x => x.Name == "m_elements");
            if (realList != null && realList.FieldType is GenericInstanceType git)
            {
                GenericInstanceType listOfEl = new GenericInstanceType(git.ElementType); listOfEl.GenericArguments.Add(el);
                FieldDefinition shadow = new FieldDefinition("m_elements_compat", FieldAttributes.Public, listOfEl);
                grid.Fields.Add(shadow);
                MethodReference listCtor = new MethodReference(".ctor", module.TypeSystem.Void, listOfEl) { HasThis = true };
                foreach (MethodDefinition gctor in grid.Methods.Where(x => x.IsConstructor && !x.IsStatic && x.HasBody))
                {
                    ILProcessor il = gctor.Body.GetILProcessor();
                    Instruction first = gctor.Body.Instructions[0];
                    il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(first, il.Create(OpCodes.Newobj, module.ImportReference(listCtor)));
                    il.InsertBefore(first, il.Create(OpCodes.Stfld, shadow));
                }
            }
            _added++;
            Log.LogInfo($"InventoryGrid.Element (wrapper, {mirrored.Count + 2} pol) + przeciazenia GetElement/GetHoveredElement + pusty List<Element> m_elements_compat.");
        }

        /// <summary>
        /// Metoda interfejsu dostaje cialo z wartoscia domyslna (default interface method) - klasy ze starych modow, ktore jej nie
        /// implementuja, przestaja padac przy ladowaniu typu (TypeLoadException: VTable setup failed), a gra woła domyslne 0/false/null.
        /// </summary>
        private static void DefaultInterfaceMethod(ModuleDefinition module, string ifaceName, string methodName)
        {
            TypeDefinition iface = module.GetType(ifaceName);
            MethodDefinition md = iface?.Methods.FirstOrDefault(x => x.Name == methodName);
            if (md == null) { Log.LogWarning($"Brak {ifaceName}.{methodName}."); return; }
            if (md.HasBody) { Log.LogInfo($"{ifaceName}.{methodName} ma juz cialo."); return; }

            md.IsAbstract = false; md.IsVirtual = true;
            ILProcessor il = md.Body.GetILProcessor();
            TypeReference rt = md.ReturnType;
            if (rt.MetadataType == MetadataType.Void) { }
            else if (rt.MetadataType == MetadataType.Single) il.Append(il.Create(OpCodes.Ldc_R4, 0f));
            else if (rt.MetadataType == MetadataType.Double) il.Append(il.Create(OpCodes.Ldc_R8, 0d));
            else if (rt.MetadataType == MetadataType.Int64) il.Append(il.Create(OpCodes.Ldc_I8, 0L));
            else if (rt.IsValueType && !rt.IsPrimitive)
            {
                VariableDefinition local = new VariableDefinition(rt); md.Body.Variables.Add(local); md.Body.InitLocals = true;
                il.Append(il.Create(OpCodes.Ldloca, local)); il.Append(il.Create(OpCodes.Initobj, rt)); il.Append(il.Create(OpCodes.Ldloc, local));
            }
            else if (rt.IsValueType) il.Append(il.Create(OpCodes.Ldc_I4_0));
            else il.Append(il.Create(OpCodes.Ldnull));
            il.Append(il.Create(OpCodes.Ret));
            _added++;
            Log.LogInfo($"{ifaceName}.{methodName}: domyslna implementacja w interfejsie (dla klas ze starych modow).");
        }

        /// <summary>Pusta metoda publiczna - cel dla patchy Harmony, ktorych oryginal zniknal z gry (funkcja moda po prostu nie zadziala, ale mod sie zaladuje).</summary>
        private static void InjectMethodStub(ModuleDefinition module, string typeName, string methodName)
        {
            TypeDefinition type = module.GetType(typeName);
            if (type == null) { Log.LogWarning($"Brak typu {typeName}."); return; }
            if (type.Methods.Any(x => x.Name == methodName)) { Log.LogInfo($"{typeName}.{methodName} istnieje - stub pominiety."); return; }
            MethodDefinition stub = new MethodDefinition(methodName, MethodAttributes.Public | MethodAttributes.HideBySig, module.ImportReference(typeof(void)));
            stub.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(stub);
            _added++;
            Log.LogInfo($"{typeName}.{methodName}(): pusty stub (w 1.0 tej metody nie ma).");
        }

        /// <summary>Dodaje Type.Awake() i wola je na poczatku OnEnable - mody patchujace Awake (ktorego w 1.0 juz nie ma) dostaja swoj postfix przy kazdym wlaczeniu.</summary>
        private static void InjectAwakeCalledFromOnEnable(ModuleDefinition module, string typeName)
        {
            TypeDefinition type = module.GetType(typeName);
            if (type == null) { Log.LogWarning($"Brak typu {typeName}."); return; }
            if (type.Methods.Any(x => x.Name == "Awake")) { Log.LogInfo($"{typeName}.Awake istnieje."); return; }
            MethodDefinition onEnable = type.Methods.FirstOrDefault(x => x.Name == "OnEnable" && x.HasBody);
            if (onEnable == null) { Log.LogWarning($"{typeName} nie ma OnEnable - Awake pominiete."); return; }

            MethodDefinition awake = new MethodDefinition("Awake", MethodAttributes.Public | MethodAttributes.HideBySig, module.ImportReference(typeof(void)));
            awake.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            type.Methods.Add(awake);
            ILProcessor il = onEnable.Body.GetILProcessor();
            Instruction first = onEnable.Body.Instructions[0];
            il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(first, il.Create(OpCodes.Call, awake));
            _added++;
            Log.LogInfo($"{typeName}.Awake() dodane i wolane z OnEnable.");
        }

        // ------------------------------------------------------------------------------------------------
        private static MethodReference _stableHash;

        /// <summary>string.GetStableHashCode() z assembly_utils - do konwersji nazwa prefabu -> hash.</summary>
        private static MethodReference StableHash(ModuleDefinition module)
        {
            if (_stableHash != null) return _stableHash;
            AssemblyDefinition utils = module.AssemblyResolver.Resolve(new AssemblyNameReference("assembly_utils", null));
            TypeDefinition ext = utils.MainModule.GetType("StringExtensionMethods");
            MethodDefinition md = ext.Methods.First(x => x.Name == "GetStableHashCode" && x.Parameters.Count == 1);
            return _stableHash = module.ImportReference(md);
        }

        /// <summary>
        /// Dodaje do typu stare przeciazenie (parametry oldTypes), ktore wola nowa metode o tej samej nazwie. spec opisuje kolejne
        /// parametry nowej metody: indeks starego parametru, literal (bool/Lit) albo D = wartosc domyslna. Stary string -> nowy int
        /// przechodzi przez GetStableHashCode (nazwa prefabu -> hash).
        /// </summary>
        private static void Fwd(ModuleDefinition module, string typeName, string methodName, string[] oldTypes, object[] spec)
        {
            TypeDefinition type = module.GetType(typeName);
            if (type == null) { Log.LogWarning($"Brak typu {typeName} - pomijam {methodName}."); return; }

            MethodDefinition target = type.Methods.FirstOrDefault(x => x.Name == methodName && x.Parameters.Count == spec.Length &&
                !x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(oldTypes) && SpecFits(x, oldTypes, spec));
            if (target == null) { Log.LogWarning($"Brak {typeName}.{methodName} z {spec.Length} parametrami pasujacymi do specyfikacji - gra znow zmienila API, most pominiety."); return; }

            if (type.Methods.Any(x => x.Name == methodName && x.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(oldTypes)))
            { Log.LogInfo($"{typeName}.{methodName}({string.Join(",", oldTypes)}) juz istnieje."); return; }

            MethodAttributes attrs = MethodAttributes.Public | MethodAttributes.HideBySig;
            if (target.IsConstructor) attrs |= MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
            if (target.IsStatic) attrs |= MethodAttributes.Static;
            MethodDefinition bridge = new MethodDefinition(methodName, attrs, target.ReturnType);
            for (int i = 0; i < oldTypes.Length; i++)
            {
                int newIdx = Array.FindIndex(spec, s => s is int k && k == i);
                TypeReference pt = newIdx >= 0 && oldTypes[i] != "System.String" ? target.Parameters[newIdx].ParameterType : ResolveType(module, oldTypes[i]);
                bridge.Parameters.Add(new ParameterDefinition(newIdx >= 0 ? target.Parameters[newIdx].Name : "p" + i, ParameterAttributes.None, pt));
            }

            ILProcessor il = bridge.Body.GetILProcessor();
            if (!target.IsStatic) il.Append(il.Create(OpCodes.Ldarg_0));
            for (int i = 0; i < spec.Length; i++)
            {
                TypeReference pt = target.Parameters[i].ParameterType;
                object s = spec[i];
                if (s is int idx)
                {
                    il.Append(il.Create(OpCodes.Ldarg, bridge.Parameters[idx]));
                    if (oldTypes[idx] == "System.String" && pt.MetadataType == MetadataType.Int32) il.Append(il.Create(OpCodes.Call, StableHash(module)));
                }
                else if (s is bool b) il.Append(il.Create(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
                else if (s is Lit lit)
                {
                    if (pt.MetadataType == MetadataType.Int64) il.Append(il.Create(OpCodes.Ldc_I8, lit.V));
                    else il.Append(il.Create(OpCodes.Ldc_I4, (int)lit.V));
                }
                else if (pt.IsValueType && !pt.IsPrimitive && pt.MetadataType != MetadataType.Boolean)
                {
                    VariableDefinition local = new VariableDefinition(pt);
                    bridge.Body.Variables.Add(local); bridge.Body.InitLocals = true;
                    il.Append(il.Create(OpCodes.Ldloca, local)); il.Append(il.Create(OpCodes.Initobj, pt)); il.Append(il.Create(OpCodes.Ldloc, local));
                }
                else if (pt.IsValueType)
                {
                    if (pt.MetadataType == MetadataType.Int64 || pt.MetadataType == MetadataType.UInt64) il.Append(il.Create(OpCodes.Ldc_I8, 0L));
                    else if (pt.MetadataType == MetadataType.Single) il.Append(il.Create(OpCodes.Ldc_R4, 0f));
                    else if (pt.MetadataType == MetadataType.Double) il.Append(il.Create(OpCodes.Ldc_R8, 0d));
                    else il.Append(il.Create(OpCodes.Ldc_I4_0));
                }
                else il.Append(il.Create(OpCodes.Ldnull));
            }
            il.Append(il.Create(OpCodes.Call, target));
            il.Append(il.Create(OpCodes.Ret));
            type.Methods.Add(bridge);
            _added++;
            Log.LogInfo($"{typeName}.{methodName}({string.Join(", ", oldTypes.Select(Short))}) -> {methodName} z {spec.Length} parametrami.");
        }

        /// <summary>Czy nowa metoda pasuje do specyfikacji: parametry mapowane ze starych maja ten sam typ (albo string->int przez hash).</summary>
        private static bool SpecFits(MethodDefinition candidate, string[] oldTypes, object[] spec)
        {
            for (int i = 0; i < spec.Length; i++)
            {
                if (!(spec[i] is int idx)) continue;
                string want = oldTypes[idx], have = candidate.Parameters[i].ParameterType.FullName;
                if (want == have) continue;
                if (want == "System.String" && have == "System.Int32") continue;
                return false;
            }
            return true;
        }

        private static TypeReference ResolveType(ModuleDefinition module, string fullName)
        {
            if (fullName == "System.String") return module.TypeSystem.String;
            if (fullName == "System.Int32") return module.TypeSystem.Int32;
            if (fullName == "System.Boolean") return module.TypeSystem.Boolean;
            TypeDefinition t = module.GetType(fullName);
            if (t != null) return t;
            throw new InvalidOperationException("Nie umiem rozwiazac typu " + fullName);
        }

        private static string Short(string fullName) => fullName.Substring(fullName.LastIndexOfAny(new[] { '.', '/' }) + 1);
    }
}

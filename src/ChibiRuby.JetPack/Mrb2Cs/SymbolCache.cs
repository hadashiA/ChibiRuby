using System.Collections.Generic;
namespace ChibiRuby.JetPack.Mrb2Cs;

// Interns each distinct symbol literal once into a per-method static field. Pure data + slot
// allocation: assigns each distinct literal a stable slot and a readable C# field name, and hands
// that data back to callers. Turning the slots into actual C# field declarations / the intern
// prologue is the Emitter's job (Emitter.EmitSymbolFields / EmitSymbolInit).
sealed class SymbolCache(string methodName)
{
    readonly Dictionary<string, int> slots = new();
    readonly List<string> literals = [];
    readonly List<string> fieldNames = []; // slot -> static field name (slot + readable name)

    // Returns the static field expression for `stringLiteral`, allocating a slot.
    public string Reference(string stringLiteral)
    {
        if (!slots.TryGetValue(stringLiteral, out var slot))
        {
            slot = literals.Count;
            slots[stringLiteral] = slot;
            literals.Add(stringLiteral);
            // The slot index keeps the name unique (two symbols that sanitize to the same
            // token still get distinct slots); the suffix just makes it readable.
            fieldNames.Add(methodName + "__sym" + slot + SanitizedSuffix(stringLiteral));
        }
        return fieldNames[slot];
    }

    public int Count => literals.Count;

    // Data exposed for the Emitter to turn into field declarations / the intern prologue.
    public string MethodName => methodName;
    public IReadOnlyList<string> FieldNames => fieldNames;
    public IReadOnlyList<string> Literals => literals;

    // Build a readable `_name` suffix from a C# string literal of a Ruby symbol, mapping
    // characters that aren't legal in a C# identifier to lowercase words (e.g. `@x` -> `_at_x`,
    // `empty?` -> `_empty_q`, `<=>` -> `_lt_eq_gt`). The slot index already guarantees uniqueness;
    // an all-illegal name (e.g. `+`) still gets a readable token, never an empty suffix.
    static string SanitizedSuffix(string stringLiteral) => "_" + Emitter.SanitizeToIdentifier(Emitter.RawName(stringLiteral));
}

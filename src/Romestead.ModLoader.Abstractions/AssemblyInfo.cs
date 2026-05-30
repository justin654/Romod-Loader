using System.Runtime.CompilerServices;

// The mod-facing contract is its own assembly, but a handful of members
// (e.g. ModRegistries capability-state setters) are loader-internal: mods must
// not call them, yet the loader assemblies that used to compile these sources
// in directly still need access. Grant exactly those trusted assemblies.
// Mods reference this assembly but are NOT on this list, so the internals stay
// hidden from them.
[assembly: InternalsVisibleTo("Romestead.StartupHook")]
[assembly: InternalsVisibleTo("Romestead.ModLoader.ClientCore")]

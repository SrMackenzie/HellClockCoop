using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
// uso: refcheck <pasta Managed do jogo> <dll1> <dll2> ...
// Lista MemberRefs para mscorlib/System/System.Core que não existem (nome + nº de parâmetros) nas DLLs do jogo.
var managed = args[0];
var targets = new Dictionary<string, Dictionary<string, HashSet<string>>>(); // asm -> type -> "name/argc"
Dictionary<string, HashSet<string>> Load(string asm) {
  if (targets.TryGetValue(asm, out var d)) return d;
  d = new(); targets[asm] = d;
  var p = Path.Combine(managed, asm + ".dll"); if (!File.Exists(p)) return d;
  using var pe = new PEReader(File.OpenRead(p)); var md = pe.GetMetadataReader();
  foreach (var th in md.TypeDefinitions) {
    var t = md.GetTypeDefinition(th); var name = FullName(md, t);
    var set = new HashSet<string>(); d[name] = set;
    foreach (var mh in t.GetMethods()) { var m = md.GetMethodDefinition(mh); set.Add(md.GetString(m.Name) + "/" + m.DecodeSignature(new Prov(), null).ParameterTypes.Length); }
    foreach (var fh in t.GetFields()) set.Add(md.GetString(md.GetFieldDefinition(fh).Name) + "/f");
  }
  // type forwards
  foreach (var eh in md.ExportedTypes) { var e = md.GetExportedType(eh); var n = (md.GetString(e.Namespace) is var ns && ns.Length>0 ? ns+"." : "") + md.GetString(e.Name); d.TryAdd(n, null); }
  return d;
}
string FullName(MetadataReader md, TypeDefinition t) {
  var n = md.GetString(t.Name); var ns = md.GetString(t.Namespace);
  if (t.IsNested) return FullName(md, md.GetTypeDefinition(t.GetDeclaringType())) + "+" + n;
  return ns.Length > 0 ? ns + "." + n : n;
}
string RefName(MetadataReader md, TypeReferenceHandle h, out string asm) {
  var r = md.GetTypeReference(h); var n = md.GetString(r.Name); var ns = md.GetString(r.Namespace);
  if (r.ResolutionScope.Kind == HandleKind.TypeReference) { var outer = RefName(md, (TypeReferenceHandle)r.ResolutionScope, out asm); return outer + "+" + n; }
  asm = r.ResolutionScope.Kind == HandleKind.AssemblyReference ? md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)r.ResolutionScope).Name) : "?";
  return ns.Length > 0 ? ns + "." + n : n;
}
var interesting = new HashSet<string>{"mscorlib","System","System.Core","netstandard"};
foreach (var file in args.Skip(1)) {
  using var pe = new PEReader(File.OpenRead(file)); var md = pe.GetMetadataReader();
  var missing = new SortedSet<string>();
  foreach (var trh in md.TypeReferences) {
    var tn = RefName(md, trh, out var asm); if (!interesting.Contains(asm)) continue;
    bool found = false; foreach (var a in new[]{asm,"mscorlib","System","System.Core"}) if (Load(a).ContainsKey(tn)) { found = true; break; }
    if (!found) missing.Add($"TIPO {asm}:{tn}");
  }
  foreach (var mrh in md.MemberReferences) {
    var mr = md.GetMemberReference(mrh);
    if (mr.Parent.Kind != HandleKind.TypeReference) continue;
    var tn = RefName(md, (TypeReferenceHandle)mr.Parent, out var asm); if (!interesting.Contains(asm)) continue;
    var name = md.GetString(mr.Name);
    string key = mr.GetKind() == MemberReferenceKind.Method ? name + "/" + mr.DecodeMethodSignature(new Prov(), null).ParameterTypes.Length : name + "/f";
    bool found = false;
    foreach (var a in new[]{asm,"mscorlib","System","System.Core"}) { var d = Load(a); if (d.TryGetValue(tn, out var set) && set != null && set.Contains(key)) { found = true; break; } }
    if (!found) missing.Add($"{tn}::{key}");
  }
  Console.WriteLine($"== {Path.GetFileName(file)}: {missing.Count} ausentes");
  foreach (var m in missing) Console.WriteLine("   " + m);
}
class Prov : ISignatureTypeProvider<string, object> {
  public string GetArrayType(string e, ArrayShape s) => e+"[]"; public string GetByReferenceType(string e) => e+"&";
  public string GetFunctionPointerType(MethodSignature<string> s) => "fn"; public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) => g;
  public string GetGenericMethodParameter(object c, int i) => "!!"+i; public string GetGenericTypeParameter(object c, int i) => "!"+i;
  public string GetModifiedType(string m, string u, bool r) => u; public string GetPinnedType(string e) => e; public string GetPointerType(string e) => e+"*";
  public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString(); public string GetSZArrayType(string e) => e+"[]";
  public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => "T"; public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => "T";
  public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => "T";
}

using System.Reflection;
using VCanPLib;

Console.WriteLine("=== RawDataCan ===");
var t = typeof(RawDataCan);
foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
    Console.WriteLine($"PROP {p.PropertyType.FullName} {p.Name}");
foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
    Console.WriteLine($"FIELD {f.FieldType.FullName} {f.Name}");

Console.WriteLine("=== VCANPCtrl Methods ===");
foreach (var m in typeof(VCANPCtrl).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(m => m.Name))
{
    var ps = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
    Console.WriteLine($"METHOD {m.ReturnType.Name} {m.Name}({ps})");
}

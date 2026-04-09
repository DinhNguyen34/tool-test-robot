using System;
using System.Linq;
using System.Reflection;
class P {
  static void Main() {
    var asm = Assembly.LoadFrom(@"C:\Users\LAB\Downloads\VCanPLib\VCanPLib.dll");
    var t = asm.GetType("VCanPLib.RawDataCan");
    Console.WriteLine(t == null ? "TYPE_NOT_FOUND" : t.FullName);
    if (t == null) return;
    foreach (var p in t.GetProperties(BindingFlags.Public|BindingFlags.Instance|BindingFlags.NonPublic)) Console.WriteLine($"PROP {p.PropertyType.FullName} {p.Name}");
    foreach (var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.NonPublic)) Console.WriteLine($"FIELD {f.FieldType.FullName} {f.Name}");
  }
}

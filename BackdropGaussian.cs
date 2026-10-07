using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Effects;
using Windows.UI.Composition;
using Windows.Foundation;
using System.Runtime.InteropServices.WindowsRuntime;
// Describe a Direct2D Gaussian blur to Windows Composition without a Win2D dependency.
[ComVisible(true)]
public class BackdropGaussian : IGraphicsEffect, IGraphicsEffectSource, IGraphicsEffectD2D1Interop {
 public string Name{get;set;}
 readonly IGraphicsEffectSource source=new CompositionEffectSourceParameter("Backdrop");
 public void GetEffectId(out Guid id){id=new Guid("1FEB6D69-2FE6-4AC9-8C58-1D7F93E7A6A5");}
 public void GetNamedPropertyMapping(string name,out uint index,out uint mapping){
  if(name!="StandardDeviation")throw new ArgumentException("Unknown Gaussian property","name");index=0;mapping=1;
 }
 public void GetPropertyCount(out uint count){count=3;}
 readonly IPropertyValueStatics values=(IPropertyValueStatics)WindowsRuntimeMarshal.GetActivationFactory(typeof(PropertyValue));
 public void GetProperty(uint index,out IntPtr value){
  switch(index){case 0:values.CreateSingle(10f,out value);break;case 1:values.CreateUInt32(1,out value);break;case 2:values.CreateUInt32(1,out value);break;default:throw new ArgumentOutOfRangeException("index");}
 }
 public void GetSource(uint index,out IGraphicsEffectSource value){if(index!=0)throw new ArgumentOutOfRangeException("index");value=source;}
 public void GetSourceCount(out uint count){count=1;}
}
[ComImport,Guid("2FC57384-A068-44D7-A331-30982FCF7177"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IGraphicsEffectD2D1Interop {
 void GetEffectId(out Guid id);
 void GetNamedPropertyMapping([MarshalAs(UnmanagedType.LPWStr)]string name,out uint index,out uint mapping);
 void GetPropertyCount(out uint count);
 void GetProperty(uint index,out IntPtr value);
 void GetSource(uint index,out IGraphicsEffectSource value);
 void GetSourceCount(out uint count);
}


[ComImport,Guid("629BDBC8-D932-4FF4-96B9-8D96C5C1E858"),InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
interface IPropertyValueStatics {
 void CreateEmpty(out IntPtr value);
 void CreateUInt8(byte v,out IntPtr value);
 void CreateInt16(short v,out IntPtr value);
 void CreateUInt16(ushort v,out IntPtr value);
 void CreateInt32(int v,out IntPtr value);
 void CreateUInt32(uint v,out IntPtr value);
 void CreateInt64(long v,out IntPtr value);
 void CreateUInt64(ulong v,out IntPtr value);
 void CreateSingle(float v,out IntPtr value);
}

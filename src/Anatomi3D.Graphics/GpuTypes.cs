using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Anatomi3D.Graphics;

// HLSL cbuffer/StructuredBuffer düzenleriyle birebir aynı C# yapıları (Common.hlsli, Post.hlsl, Env.hlsl).
// cbuffer'larda her satır 16 bayttır; alanlar aynı sırayla ve aynı türle tanımlanmalıdır.

[InlineArray(16)]
public struct Float4x16 { private Vector4 e; }

[InlineArray(9)]
public struct Float4x9 { private Vector4 e; }

[StructLayout(LayoutKind.Sequential)]
public struct FrameConstants
{
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Matrix4x4 ViewProj;
    public Matrix4x4 InvViewProj;
    public Matrix4x4 ShadowViewProj;
    public Matrix4x4 EnvRotation;
    public Vector3 CameraPos; public float Time;
    public Vector2 ViewSize; public Vector2 InvViewSize;
    public Vector3 QuantMin; public float TessTargetPx;
    public Vector3 QuantScale; public float TessMaxFactor;
    public Vector4 ClipPlane;
    public Vector3 KeyDir; public float KeyIntensity;
    public Vector3 KeyColor; public float ShadowStrength;
    public Vector3 FillDir; public float FillIntensity;
    public Vector3 FillColor; public float RimIntensity;
    public Vector3 RimDir; public float EnvIntensity;
    public Vector3 RimColor; public float DetailStrength;
    public Float4x16 Holes;
    public uint HoleCount; public uint FrameFlags; public float ShadowTexel; public float ShadowNormalOffset;
    public Float4x9 EnvSH;
    public Vector3 SelectColor; public float SelectPulse;
    public Vector3 HoverColor; public float Exposure;
    public Vector3 QuizColor; public float GhostOpacityScale;
    public Vector3 EdgeColor; public float SpecularAA;
}

[Flags]
public enum FrameFlags : uint
{
    None = 0,
    Clip = 1,
    Shadows = 2,
    Ssao = 4,
    Detail = 8,
    Wire = 16,
}

[StructLayout(LayoutKind.Sequential)]
public struct PostConstants
{
    public Vector4 TexelSize;
    public Vector4 ProjInfo;
    public float AoRadius; public float AoIntensity; public float AoProjScale; public uint SampleCount;
    public float BloomIntensity; public float BloomThreshold; public float Vignette; public float Grain;
    public uint SelectedId; public uint HoveredId; public uint QuizId; public uint OutlineFlags;
    public Vector3 BgTop; public float Contrast;
    public Vector3 BgBottom; public float Saturation;
    public Vector2 BlurDir; public float UpsampleRadius; public float PostPad;
}

[StructLayout(LayoutKind.Sequential)]
public struct EnvConstants
{
    public uint Face;
    public float Roughness;
    public float SrcSize;
    public float Pad;
}

/// <summary>Kareden kareye değişen yapı durumu (GPU StructuredBuffer, 80 bayt).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PartStateGpu
{
    public Vector3 Pivot; public float Scale;
    public Vector3 DigOrigin; public float Dissolve;
    public Vector3 DigDir; public float DigRadius;
    public Vector3 Tint; public float TintAmount;
    public float Opacity; public uint Flags; public float Emissive;
    /// <summary>Kesit kapağı derinlik kaydırması (m): iç yapıların kapağı dıştakilerin önünde kalsın</summary>
    public float CapBias;
}

[Flags]
public enum PartStateFlags : uint
{
    None = 0,
    Sweep = 1,
    Windowed = 2,
    Selected = 4,
    Hovered = 8,
    Quiz = 16,
    AttachOrigin = 32,
    AttachInsertion = 64,
}

/// <summary>Yapının malzeme parametreleri (GPU StructuredBuffer, 80 bayt).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct PartMaterialGpu
{
    public Vector3 Albedo; public float Roughness;
    public Vector3 Sheen; public float Clearcoat;
    public Vector3 Center; public float Param;
    public Vector3 Axis; public uint Family;
    public uint Kind; public float Bump; public float Freq; public float Subsurface;
}

/// <summary>Prosedürel yüzey ailesi (Geometry.hlsl ComputeDetail).</summary>
public enum SurfaceFamily : uint
{
    None = 0,
    Fiber = 1,
    Bone = 2,
    Skin = 3,
    Wet = 4,
    Eye = 5,
    Glass = 6,
}

/// <summary>Yapının bu karede nasıl çizileceği.</summary>
public enum PartRender : byte
{
    Hidden = 0,
    Opaque = 1,
    /// <summary>Yarı saydam (X-ray / çevreyi saydamlaştır) — sıra bağımsız saydamlık geçişi</summary>
    Ghost = 2,
    /// <summary>Kornea, lens: yansımalı cam</summary>
    Glass = 3,
}

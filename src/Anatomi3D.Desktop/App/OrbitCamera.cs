using System.Numerics;
using Anatomi3D.Graphics;

namespace Anatomi3D.Desktop.App;

/// <summary>
/// Yörünge kamerası: hedef etrafında döndürme, kaydırma, imlece doğru yakınlaştırma; tüm hareketler kritik
/// sönümlü yumuşatmayla (bırakınca atalet) ve animasyonlu geçişlerle (odakla, hazır görünümler).
/// Model uzayı: +Y yukarı, +Z vücudun önü, +X anatomik sol.
/// </summary>
public sealed class OrbitCamera
{
    // hedef (istenen) durum
    public Vector3 Target = new(0, 0.92f, 0);
    public float Distance = 4.1f;
    public float Yaw;
    public float Pitch = 0.02f;

    // çizilen (yumuşatılmış) durum
    private Vector3 target;
    private float distance, yaw, pitch;
    private Vector2 orbitVelocity;
    private bool dragging;

    public float FovY { get; set; } = 30f * MathF.PI / 180f;
    public float MinDistance { get; set; } = 0.06f;
    public float MaxDistance { get; set; } = 9f;

    // animasyonlu geçiş
    private (Vector3 t, float d, float y, float p)? flyFrom, flyTo;
    private float flyT, flyDuration;

    public OrbitCamera()
    {
        Snap();
    }

    public void Snap()
    {
        target = Target;
        distance = Distance;
        yaw = Yaw;
        pitch = Pitch;
    }

    public Vector3 CurrentTarget => target;
    public float CurrentDistance => distance;
    public bool IsAnimating => flyTo != null || orbitVelocity.LengthSquared() > 1e-6f ||
                               Vector3.DistanceSquared(target, Target) > 1e-10f || MathF.Abs(distance - Distance) > 1e-5f * Distance ||
                               MathF.Abs(yaw - Yaw) > 1e-5f || MathF.Abs(pitch - Pitch) > 1e-5f;

    public Vector3 Position => target + Forward(yaw, pitch) * -distance;

    private static Vector3 Forward(float yaw, float pitch)
    {
        // yaw = 0: kamera +Z'de, -Z'ye bakar
        float cp = MathF.Cos(pitch);
        return -new Vector3(MathF.Sin(yaw) * cp, MathF.Sin(pitch), MathF.Cos(yaw) * cp);
    }

    public void BeginDrag() { dragging = true; orbitVelocity = Vector2.Zero; flyTo = null; }
    public void EndDrag() => dragging = false;

    /// <summary>Fare sürükleme ile döndürme (piksel).</summary>
    public void Orbit(float dx, float dy, float viewportHeight)
    {
        float k = 3.2f / Math.Max(200f, viewportHeight);
        Yaw -= dx * k;
        Pitch = Math.Clamp(Pitch + dy * k, -1.52f, 1.52f);
        orbitVelocity = new Vector2(-dx * k, dy * k) * 60f;
        flyTo = null;
    }

    /// <summary>Kaydırma: ekran düzleminde, uzaklıkla orantılı.</summary>
    public void Pan(float dx, float dy, float viewportHeight)
    {
        float worldPerPx = 2f * Distance * MathF.Tan(FovY * 0.5f) / Math.Max(1f, viewportHeight);
        var fwd = Forward(Yaw, Pitch);
        var right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        var up = Vector3.Cross(right, fwd);
        Target += (-right * dx + up * dy) * worldPerPx;
        flyTo = null;
    }

    /// <summary>Tekerlek ile yakınlaştırma; <paramref name="focus"/> verilirse o noktaya doğru.</summary>
    public void Zoom(float wheel, Vector3? focus)
    {
        float f = MathF.Pow(0.86f, wheel);
        float nd = Math.Clamp(Distance * f, MinDistance, MaxDistance);
        if (focus.HasValue && wheel > 0)
        {
            // imlecin altındaki noktaya doğru kay (yakınlaşırken)
            float t = 1f - nd / Distance;
            Target = Vector3.Lerp(Target, focus.Value, t);
        }
        Distance = nd;
        flyTo = null;
    }

    public void FlyTo(Vector3 tgt, float dist, float? yawTo = null, float? pitchTo = null, float duration = 0.8f)
    {
        flyFrom = (target, distance, yaw, pitch);
        float y = yawTo ?? Yaw;
        // en kısa yoldan dön
        float cy = yaw;
        while (y - cy > MathF.PI) y -= 2 * MathF.PI;
        while (y - cy < -MathF.PI) y += 2 * MathF.PI;
        flyTo = (tgt, Math.Clamp(dist, MinDistance, MaxDistance), y, Math.Clamp(pitchTo ?? Pitch, -1.52f, 1.52f));
        flyT = 0;
        flyDuration = duration;
        orbitVelocity = Vector2.Zero;
    }

    public void Update(float dt)
    {
        if (flyTo is { } to && flyFrom is { } from)
        {
            flyT += dt / flyDuration;
            float t = Math.Clamp(flyT, 0, 1);
            float e = t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
            Target = Vector3.Lerp(from.t, to.t, e);
            // uzaklık logaritmik ölçekte (yakın/uzak geçişleri doğal)
            Distance = MathF.Exp(float.Lerp(MathF.Log(from.d), MathF.Log(to.d), e));
            Yaw = float.Lerp(from.y, to.y, e);
            Pitch = float.Lerp(from.p, to.p, e);
            target = Target; distance = Distance; yaw = Yaw; pitch = Pitch;
            if (t >= 1) { flyTo = null; flyFrom = null; }
            return;
        }
        // bırakıldıktan sonra atalet
        if (!dragging && orbitVelocity.LengthSquared() > 1e-6f)
        {
            Yaw += orbitVelocity.X * dt;
            Pitch = Math.Clamp(Pitch + orbitVelocity.Y * dt, -1.52f, 1.52f);
            orbitVelocity *= MathF.Exp(-dt * 7f);
        }
        float k = 1f - MathF.Exp(-dt * 18f);
        target = Vector3.Lerp(target, Target, k);
        distance = float.Lerp(distance, Distance, k);
        yaw = float.Lerp(yaw, Yaw, k);
        pitch = float.Lerp(pitch, Pitch, k);
    }

    public CameraState State(float aspect)
    {
        var pos = Position;
        var fwd = Forward(yaw, pitch);
        var view = Matrix4x4.CreateLookAt(pos, pos + fwd, Vector3.UnitY);
        // yakın/uzak düzlem: derinlik hassasiyetini uzaklığa göre koru
        float near = Math.Clamp(distance * 0.02f, 0.004f, 0.08f);
        float far = distance + 6f;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(FovY, aspect, near, far);
        return new CameraState { View = view, Proj = proj, Position = pos, FovY = FovY, Near = near, Far = far };
    }

    /// <summary>Ekran noktasından dünya ışını.</summary>
    public (Vector3 origin, Vector3 dir) Ray(float x, float y, int w, int h)
    {
        var st = State(w / (float)Math.Max(1, h));
        float nx = x / w * 2 - 1, ny = 1 - y / h * 2;
        var vp = st.View * st.Proj;
        Matrix4x4.Invert(vp, out var inv);
        var a = Vector4.Transform(new Vector4(nx, ny, 0, 1), inv);
        var b = Vector4.Transform(new Vector4(nx, ny, 1, 1), inv);
        var pa = new Vector3(a.X, a.Y, a.Z) / a.W;
        var pb = new Vector3(b.X, b.Y, b.Z) / b.W;
        return (pa, Vector3.Normalize(pb - pa));
    }
}

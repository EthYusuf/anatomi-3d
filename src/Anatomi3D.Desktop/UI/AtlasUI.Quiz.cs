using System.Numerics;
using Anatomi3D.Core.Content;
using Anatomi3D.Core.Model;
using Anatomi3D.Desktop.App;
using Anatomi3D.Graphics;
using ImGuiNET;

namespace Anatomi3D.Desktop.UI;

public sealed partial class AtlasUI
{
    private readonly Random rng = new();

    /// <summary>Şu an görünen ve sorulabilecek yapılar (deri kapalıyken yalnız deri bölgeleri).</summary>
    private List<Part> QuizCandidates()
    {
        bool skinOn = !S.HiddenCats.Contains(Category.Skin) && !Categories.IsPeeled(Category.Skin, S.PeelStep) &&
                      S.ViewMode == ViewMode.Solid && S.ClipAxis == ClipAxis.None && S.Isolated == null;
        return Model.Parts.Where(p =>
            !p.IsAttachment && p.Radius > 0.012f && p.Category != Category.Hair &&
            (skinOn ? p.Category == Category.Skin : p.Category != Category.Skin) &&
            !S.HiddenCats.Contains(p.Category) && !S.HiddenParts.Contains(p.Index) && !Categories.IsPeeled(p.Category, S.PeelStep) &&
            (S.Isolated == null || S.Isolated.Contains(p.Index)) && !Appearances.Of(p).Glass).ToList();
    }

    public void StartQuiz()
    {
        S.Select(-1);
        var q = new QuizState();
        var c = QuizCandidates();
        q.Target = c.Count > 0 ? c[rng.Next(c.Count)].Index : -1;
        S.Quiz = q;
        S.Changed();
    }

    private void NextQuestion()
    {
        var q = S.Quiz;
        if (q == null) return;
        var c = QuizCandidates().Where(p => p.Index != q.Target).ToList();
        q.Target = c.Count > 0 ? c[rng.Next(c.Count)].Index : -1;
        q.Last = null;
        q.NextAt = -1;
        S.Changed();
    }

    public void QuizPick(int part)
    {
        var q = S.Quiz;
        if (q == null || q.Target < 0 || q.Last != null || part < 0) return;
        bool ok = part == q.Target || (Model.Parts[part].En == Model.Parts[q.Target].En && Model.Parts[part].Side == Model.Parts[q.Target].Side);
        q.Total++;
        if (ok)
        {
            q.Score++;
            q.Streak++;
            q.BestStreak = Math.Max(q.BestStreak, q.Streak);
            q.NextAt = app.Time + 1.1f;
        }
        else
        {
            q.Streak = 0;
            S.RequestCamera(CameraPreset.Focus, q.Target);
        }
        q.Last = (ok, part);
        S.Changed();
    }

    private void DrawQuiz()
    {
        var q = S.Quiz;
        if (q == null) return;
        if (q.NextAt > 0 && app.Time >= q.NextAt) NextQuestion();
        float w = Theme.Px(460);
        float h = Theme.Px(q.Last is { ok: false } ? 196 : 164);
        var pos = new Vector2((Display.X - w) * 0.5f, TopH + M);
        var border = q.Last == null ? Theme.Border : q.Last.Value.ok ? Theme.Success : Theme.Danger;
        ImGui.PushStyleColor(ImGuiCol.Border, border);
        Ui.BeginPanel("##quiz", pos, new Vector2(w, h), 1, ImGuiWindowFlags.NoScrollbar);
        var dl = ImGui.GetWindowDrawList();
        ImGui.PushFont(F.BodyBold);
        ImGui.TextUnformatted(Icons.Education + "  " + L.T("quizTitle"));
        ImGui.PopFont();
        string score = $"{L.T("quizScore")}: {q.Score}/{q.Total}" + (q.Streak > 1 ? $"  ·  {L.T("quizStreak")} {q.Streak}" : "");
        ImGui.SameLine(w - ImGui.CalcTextSize(score).X - Theme.Px(56));
        Ui.TextDim(score, false);
        ImGui.SameLine(w - Theme.Px(44));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - Theme.Px(6));
        if (Ui.IconButton(Icons.Close, "quizClose", L.T("quizEnd"), false, 30)) { S.Quiz = null; S.Changed(); }

        var target = q.Target >= 0 ? Model.Parts[q.Target] : null;
        if (target == null)
        {
            Ui.TextDim(L.T("quizEmpty"));
        }
        else
        {
            ImGui.Dummy(new Vector2(0, Theme.Px(2)));
            Ui.TextMuted(L.T("quizFind"), false);
            ImGui.PushFont(F.Heading);
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(target.DisplayName(S.Lang));
            ImGui.PopTextWrapPos();
            ImGui.PopFont();
            var sub = target.SecondaryName(S.Lang);
            if (sub != null) Ui.TextMuted(sub, false);
            ImGui.Dummy(new Vector2(0, Theme.Px(4)));
            if (q.Last is { } last)
            {
                if (last.ok)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Success);
                    ImGui.TextUnformatted(Icons.Check + "  " + L.T("quizCorrect"));
                    ImGui.PopStyleColor();
                }
                else
                {
                    var picked = last.picked >= 0 ? Model.Parts[last.picked].DisplayName(S.Lang) : "—";
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Danger);
                    ImGui.PushTextWrapPos(0);
                    ImGui.TextUnformatted($"{L.T("quizWrong")} {picked}");
                    ImGui.PopTextWrapPos();
                    ImGui.PopStyleColor();
                    Ui.TextMuted(L.T("quizMissed"), false);
                    ImGui.SetCursorPos(new Vector2(w - Theme.Px(130), h - Theme.Px(48)));
                    if (Ui.Button("", L.T("quizNext") + "  →", "qnext", primary: true, width: 114, height: 34)) NextQuestion();
                }
            }
            else
            {
                ImGui.SetCursorPos(new Vector2(w - Theme.Px(212), h - Theme.Px(48)));
                if (Ui.Button("", L.T("quizReveal"), "qreveal", width: 96, height: 34))
                {
                    q.Total++;
                    q.Streak = 0;
                    q.Last = (false, -1);
                    S.RequestCamera(CameraPreset.Focus, q.Target);
                    S.Changed();
                }
                ImGui.SameLine(0, Theme.Px(8));
                if (Ui.Button("", L.T("quizSkip"), "qskip", width: 96, height: 34)) NextQuestion();
            }
        }
        Ui.EndPanel();
        ImGui.PopStyleColor();
    }
}

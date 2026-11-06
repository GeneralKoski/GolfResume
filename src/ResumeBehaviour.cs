using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace GolfResume
{
    public class ResumeBehaviour : MonoBehaviour
    {
        public ResumeBehaviour(IntPtr ptr) : base(ptr) { }

        internal static ResumeBehaviour Instance;

        static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        static string SaveDir => Path.Combine(Paths.BepInExRootPath, "GolfResume");

        IntPtr courseSeen;
        SessionRecord session;
        string sessionFile;
        bool sessionReady;
        float sessionStartedAt;
        float nextPoll;

        readonly List<float> refreshAt = new();

        readonly Dictionary<string, int> waitUntil = new();
        int catchUpUntil;
        bool enforcing;

        string message;
        float messageUntil;

        void Awake() => Instance = this;

        void Update()
        {
            try
            {
                if (Input.GetKeyDown(Plugin.RestoreKey.Value)) Restore();

                if (refreshAt.Count > 0 && Time.unscaledTime >= refreshAt[0])
                {
                    refreshAt.RemoveAt(0);
                    RefreshScoreboards();
                }

                if (Time.unscaledTime >= nextPoll)
                {
                    nextPoll = Time.unscaledTime + 1f;
                    Poll();
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogError(e);
            }
        }

        void LateUpdate()
        {
            try { EnforceWaiting(); }
            catch (Exception e) { Plugin.L.LogError(e); waitUntil.Clear(); }
        }

        void OnGUI()
        {
            if (message == null || Time.unscaledTime > messageUntil) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 22, wordWrap = true, alignment = TextAnchor.UpperLeft };
            GUI.Box(new Rect(20, 20, 720, 200), message, style);
        }

        void Say(string text)
        {
            message = "GolfResume: " + text;
            messageUntil = Time.unscaledTime + 10f;
            Plugin.L.LogInfo(text.Replace("\n", " | "));
        }

        static global::Course CurrentCourse()
        {
            try { return global::Course.Self; } catch { return null; }
        }

        static List<global::User> Users()
        {
            var list = new List<global::User>();
            var service = global::Team17.Services.Users;
            if (service == null) return list;
            var arr = service.GetUsers(out int count);
            for (int i = 0; i < count && i < arr.Length; i++)
                if (arr[i] != null) list.Add(arr[i]);
            return list;
        }

        static string IdOf(global::User u)
        {
            string id = null;
            try { id = u.PlatformIDString; } catch { }
            return string.IsNullOrEmpty(id) ? "name:" + u.DisplayName : id;
        }

        static int HoleCount(global::User u) => u.m_holeScores?.Length ?? 0;

        static global::HoleNumber Hole(int n) => new global::HoleNumber { m_Value = n };

        static int CurrentHole()
        {
            var me = global::Team17.Services.Users.GetPrimaryLocalUser();
            return me?.Ball == null ? 0 : me.Ball.HoleNumber.m_Value;
        }

        static int FirstFreeHole(global::User u)
        {
            for (int h = 1; h <= HoleCount(u); h++)
                if (!u.HasHoleScore(Hole(h))) return h;
            return HoleCount(u) + 1;
        }

        void Poll()
        {
            var course = CurrentCourse();
            if (course == null)
            {
                courseSeen = IntPtr.Zero;
                session = null;
                sessionFile = null;
                return;
            }

            if (course.Pointer != courseSeen)
            {
                courseSeen = course.Pointer;
                session = new SessionRecord { Course = course.gameObject.scene.name, Started = DateTime.Now };
                sessionFile = null;
                sessionReady = false;
                sessionStartedAt = Time.unscaledTime;
                Plugin.L.LogInfo($"New game on course {session.Course}");
            }

            if (!sessionReady)
            {
                if (Users().Any(u => Enumerable.Range(1, HoleCount(u)).Any(h => u.HasHoleScore(Hole(h))))) return;
                sessionReady = true;
            }

            bool changed = false;
            foreach (var u in Users())
            {
                string id = IdOf(u);
                int holes = HoleCount(u);
                var rec = session.Players.FirstOrDefault(p => p.Id == id);
                if (rec == null)
                {
                    rec = new PlayerRecord { Id = id, Name = u.DisplayName, Scores = new int?[holes] };
                    session.Players.Add(rec);
                }
                if (rec.Scores.Length < holes) rec.Scores = rec.Scores.Concat(new int?[holes - rec.Scores.Length]).ToArray();

                for (int h = 1; h <= holes; h++)
                {
                    int? score = u.HasHoleScore(Hole(h)) ? u.GetHoleScore(Hole(h)) : null;
                    if (score == rec.Scores[h - 1]) continue;
                    rec.Scores[h - 1] = score;
                    if (score.HasValue)
                        session.History.Add(new HoleEvent { Time = DateTime.Now, Player = rec.Name, Hole = h, Score = score.Value });
                    changed = true;
                }
            }

            if (!changed) return;
            ScheduleRefresh(0.5f);
            if (!session.Players.Any(p => p.Scores.Any(s => s.HasValue))) return;

            session.Updated = DateTime.Now;
            Directory.CreateDirectory(SaveDir);
            sessionFile ??= Path.Combine(SaveDir, $"{session.Started:yyyyMMdd-HHmmss}_{session.Course}.json");
            File.WriteAllText(sessionFile, JsonSerializer.Serialize(session, Json));
        }

        [HideFromIl2Cpp]
        void ScheduleRefresh(params float[] delays)
        {
            foreach (var d in delays) refreshAt.Add(Time.unscaledTime + d);
            refreshAt.Sort();
        }

        void RefreshScoreboards()
        {
            int hole = 0;
            try { hole = CurrentHole(); } catch { }

            foreach (var sb in Resources.FindObjectsOfTypeAll<global::ScoreboardController>())
            {
                try
                {
                    if (!sb.gameObject.scene.IsValid()) continue;
                    sb.UpdateScores(hole > 0 ? Hole(hole) : sb.m_currentHole);
                }
                catch (Exception e) { Plugin.L.LogWarning($"Scoreboard refresh failed: {e.Message}"); }
            }
        }

        static PlayerRecord Match(SessionRecord saved, global::User u)
        {
            string id = IdOf(u);
            return saved.Players.FirstOrDefault(p => p.Id == id)
                   ?? saved.Players.FirstOrDefault(p => p.Name == u.DisplayName);
        }

        static bool IsPresent(PlayerRecord rec, List<global::User> users) =>
            users.Any(u => IdOf(u) == rec.Id || u.DisplayName == rec.Name);

        [HideFromIl2Cpp]
        SessionRecord FindSave(string courseName, List<global::User> users, out string why)
        {
            why = $"no saved game for {courseName}.";
            if (!Directory.Exists(SaveDir)) return null;

            bool newest = true;
            foreach (var file in Directory.GetFiles(SaveDir, "*.json").OrderByDescending(f => f))
            {
                if (file == sessionFile) continue;
                SessionRecord s;
                try { s = JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(file)); }
                catch (Exception e) { Plugin.L.LogWarning($"Unreadable save {file}: {e.Message}"); continue; }
                if (s?.Course != courseName || !s.Players.Any(p => p.Scores.Any(x => x.HasValue))) continue;

                var absent = s.Players.Where(p => !IsPresent(p, users)).Select(p => p.Name).ToList();
                if (absent.Count == 0) return s;
                if (newest)
                    why = $"the last game on {courseName} ({s.Started:dd/MM HH:mm}) also had {string.Join(", ", absent)}, who are not here. Nothing restored.";
                newest = false;
            }
            return null;
        }

        internal void EnforceWaiting()
        {
            if (waitUntil.Count == 0 || enforcing) return;
            enforcing = true;
            try
            {
                if (CurrentCourse() == null) { waitUntil.Clear(); return; }
                int hole = CurrentHole();
                if (hole == 0) return;
                bool finished = hole >= catchUpUntil;

                foreach (var u in Users())
                {
                    if (!waitUntil.TryGetValue(IdOf(u), out int until)) continue;
                    bool wait = !finished && hole < until;
                    if (wait && !u.m_inHole) u.m_inHole = true;
                    else if (!wait && u.m_inHole && u.m_hitCounter == 0 && !u.HasHoleScore(Hole(hole))) u.m_inHole = false;
                }

                if (finished)
                {
                    waitUntil.Clear();
                    Say($"catch-up done, everyone plays from hole {hole}.");
                }
            }
            finally { enforcing = false; }
        }

        void Restore()
        {
            var course = CurrentCourse();
            if (course == null) { Say("you are not in a game."); return; }
            if (!global::PhotonNetwork.isMasterClient) { Say("only the host can restore."); return; }

            var users = Users();
            if (session == null || !sessionReady || Time.unscaledTime - sessionStartedAt < 5f || users.Any(u => u.Ball == null))
            {
                Say("the game is still loading, try again in a few seconds.");
                return;
            }

            var shot = users.Where(u => u.m_hitCounter > 0).Select(u => u.DisplayName).ToList();
            if (shot.Count > 0)
            {
                Say($"restore at the start of the game, before anyone shoots. Already shot: {string.Join(", ", shot)}.");
                return;
            }

            string courseName = course.gameObject.scene.name;
            var saved = FindSave(courseName, users, out string why);
            if (saved == null) { Say(why); return; }

            int restored = 0;
            var newcomers = new List<global::User>();
            foreach (var u in users)
            {
                var rec = Match(saved, u);
                if (rec == null) { newcomers.Add(u); continue; }

                int holes = Math.Min(HoleCount(u), rec.Scores.Length);
                for (int h = 1; h <= holes; h++)
                {
                    var s = rec.Scores[h - 1];
                    if (!s.HasValue || u.HasHoleScore(Hole(h))) continue;
                    u.SetHoleScore(Hole(h), s.Value);
                    restored++;
                }
                u.UpdateProperties();
            }

            var free = users.ToDictionary(u => u, FirstFreeHole);
            int firstFree = free.Values.Min();
            int lead = free.Values.Max();
            int maxHoles = users.Max(HoleCount);

            string text = $"restored {restored} scores from the game of {saved.Started:dd/MM HH:mm}.";
            if (newcomers.Count > 0) text += $"\nNew players: {string.Join(", ", newcomers.Select(u => u.DisplayName))}.";
            ScheduleRefresh(1f, 3f);

            if (firstFree > maxHoles) { Say(text + "\nEvery hole already has a score."); return; }

            if (lead > firstFree)
            {
                waitUntil.Clear();
                foreach (var kv in free) waitUntil[IdOf(kv.Key)] = kv.Value;
                catchUpUntil = Math.Min(lead, maxHoles + 1);
                var behind = free.Where(kv => kv.Value < catchUpUntil).Select(kv => $"{kv.Key.DisplayName} {kv.Value}-{catchUpUntil - 1}");
                text += $"\nCatch-up: {string.Join(", ", behind)}. Players who already have the hole stay in the hole and must NOT shoot.";
                if (catchUpUntil <= maxHoles) text += $"\nEveryone plays from hole {catchUpUntil}.";
            }

            if (firstFree > 1)
            {
                try
                {
                    global::Team17.Flow.GameState.SharedContextInformation.GameManagement.SetHoleNumber = Hole(firstFree);
                    text += $"\nMoving to hole {firstFree}.";
                    ScheduleRefresh(3f, 6f);
                }
                catch (Exception e)
                {
                    Plugin.L.LogWarning($"Hole jump failed: {e}");
                    text += $"\nNext hole to play: {firstFree} (automatic jump failed, use Set Hole).";
                }
            }
            Say(text);
        }
    }
}

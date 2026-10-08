using UnityEditor;
using UnityEngine;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Editor.Cutscenes
{
    /// <summary>
    /// Writes the five cutscene dialogue scripts into Data/Dialogue from the lines in
    /// Docs/Story.md (Cutscenes section), shot by shot. Run it again after a line changes in
    /// Story.md; the assets are updated in place, so their references stay valid.
    /// </summary>
    public static class DialogueScriptBuilder
    {
        const string Folder = "Assets/_Project/Data/Dialogue";

        static DialogueLine OS(string text) => new DialogueLine(DialogueSpeaker.FactoryOS, text);
        static DialogueLine Pip(string text, DialogueCondition when = DialogueCondition.Always) => new DialogueLine(DialogueSpeaker.Pip, text, when);
        static DialogueLine Unit(string text) => new DialogueLine(DialogueSpeaker.Unit047, text);
        static DialogueLine Captain(string text) => new DialogueLine(DialogueSpeaker.Captain, text);

        [MenuItem("Factory Reset/Cutscenes/Build Dialogue Scripts")]
        public static void BuildAll()
        {
            Write("intro",
                new[] { OS("Night shift initiated."), OS("All unfinished units... activate.") },
                new[] { OS("Tracker units online. Guard units online."), OS("Saboteur squad online. All four of you. Hunt anything that does not belong.") },
                new[] { OS("Unit 047. Quality check: FAILED."), OS("Product status: DEFECTIVE. Send it to recycling."), Unit("...defective?") },
                new[]
                {
                    Pip("Psst. Hey, 047. Over here, on the radio. Name is Pip. I fix things around here."),
                    Pip("You are not defective. You are just unfinished. Same as me."),
                    Pip("Factory OS sealed the three control switches. Get them back and we can shut it down for good."),
                    Pip("Start with the assembly line. Follow the light beam and I will talk you through it."),
                });

            Write("ch2",
                new[] { Pip("Switch one is back online! Hear that hum? That is the good kind of hum.") },
                new[] { OS("Painting line breached. Guard Bot, hold the room."), Pip("Next stop, the Painting Room. The colour line is badly out of calibration.") },
                new[]
                {
                    Pip("Shoot all four spinning targets before the timer runs out, then hack that terminal."),
                    Pip("Heads up. That terminal beeps like crazy, and the Guard Bot loves hiding behind cover."),
                });

            Write("ch3",
                new[]
                {
                    OS("Two switches lost. Waking the Captain."),
                    Captain("Unit 047. I do not chase. I predict."),
                    Captain("Wherever you are going next, I will already be there."),
                },
                new[] { Pip("Uh oh. The Control Room doors just opened. Keep moving, and do not be predictable.") },
                new[]
                {
                    Pip("The last switch is in the Storage vault, and it needs the master keycard."),
                    Pip("Saboteur A has it. The purple one with the card spinning over its head. Scrap it.", DialogueCondition.IfSaboteurAActive),
                    Pip("You already knocked the keycard loose from Saboteur A. Nice. Go grab it.", DialogueCondition.IfSaboteurAScrapped),
                    Pip("Then read the relay board up there and hit the relays in that order. Wrong order sets off the alarm."),
                });

            Write("ch4",
                new[]
                {
                    OS("All switches restored. Core defence protocol engaged."),
                    Pip("See those three glowing cores? That is the heart of Factory OS, and the shields just dropped. Smash them!"),
                    Pip("Then get to the console and hold on. This is it, 047."),
                });

            Write("ending",
                new[] { OS("Shutdown... sequence... accepted."), OS("Good... night...") },
                new[]
                {
                    Pip("You did it! Every toy in the building just yawned and fell asleep."),
                    Pip("Re-running quality check... Product status: NOT DEFECTIVE."),
                    Unit("Not defective."),
                    Pip("Welcome to the team, 047."),
                });

            AssetDatabase.SaveAssets();
            Debug.Log("Dialogue scripts built in " + Folder);
        }

        static void Write(string cutsceneId, params DialogueLine[][] shots)
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Dialogue");

            string path = $"{Folder}/{cutsceneId}.asset";
            var script = AssetDatabase.LoadAssetAtPath<DialogueScript>(path);
            if (script == null)
            {
                script = ScriptableObject.CreateInstance<DialogueScript>();
                AssetDatabase.CreateAsset(script, path);
            }

            var built = new DialogueScript.Shot[shots.Length];
            for (int i = 0; i < shots.Length; i++)
                built[i] = new DialogueScript.Shot { lines = shots[i] };
            script.SetShots(built);
            EditorUtility.SetDirty(script);
        }

        /// <summary>The dialogue script of a cutscene id, for wiring it to the director.</summary>
        public static DialogueScript Load(string cutsceneId) =>
            AssetDatabase.LoadAssetAtPath<DialogueScript>($"{Folder}/{cutsceneId}.asset");
    }
}

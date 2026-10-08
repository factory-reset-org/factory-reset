using System;
using System.Collections.Generic;
using ToyFactory.Interfaces;

namespace ToyFactory.Journey.Cutscenes
{
    /// <summary>
    /// Plays dialogue lines one after another, kept apart from Unity so the timing can be
    /// tested: each line types out at 38 characters per second, then holds for
    /// 1.3 s + 0.025 s per character before the next one. The advance input (Click, Space, E)
    /// finishes the typing first, and on a fully typed line moves straight to the next.
    /// </summary>
    /// <remarks>
    /// <para>Every change is reported through <see cref="DialogueEvents"/>, so the subtitles (S3's
    /// UI) only redraw what they are given. <c>Typed</c> fires once per newly typed character,
    /// for the voice blips.</para>
    /// <para>Lines with a <see cref="DialogueCondition"/> that does not hold when they come up are
    /// skipped, which is how the Chapter 3 keycard line picks its version.</para>
    /// </remarks>
    public sealed class DialogueRunner
    {
        /// <summary>Characters typed per second.</summary>
        public const float CharactersPerSecond = 38f;

        /// <summary>Hold after a line is typed: this plus <see cref="HoldPerCharacter"/> per character.</summary>
        public const float HoldBase = 1.3f;

        /// <summary>Extra hold per character, so longer lines stay up longer.</summary>
        public const float HoldPerCharacter = 0.025f;

        readonly Queue<DialogueLine> _queue = new Queue<DialogueLine>();
        readonly Func<DialogueCondition, bool> _conditionHolds;

        DialogueLine _current;
        bool _hasLine;
        float _typed;      // characters typed, fractional
        float _held;       // seconds held after typing finished
        int _shown = -1;   // characters last reported

        /// <param name="conditionHolds">Decides conditional lines; null means only unconditional lines are said.</param>
        public DialogueRunner(Func<DialogueCondition, bool> conditionHolds = null)
        {
            _conditionHolds = conditionHolds ?? (c => c == DialogueCondition.Always);
        }

        /// <summary>Raised once per newly typed character with the speaker, for the voice blips.</summary>
        public event Action<DialogueSpeaker> Typed;

        /// <summary>True while a line is on screen or waiting.</summary>
        public bool IsBusy => _hasLine || _queue.Count > 0;

        /// <summary>The line on screen; meaningful while <see cref="IsBusy"/>.</summary>
        public DialogueLine Current => _current;

        /// <summary>Seconds a fully typed line of <paramref name="characters"/> characters stays up.</summary>
        public static float HoldSeconds(int characters) => HoldBase + HoldPerCharacter * characters;

        /// <summary>Queues lines to be said after any already waiting.</summary>
        public void Enqueue(IEnumerable<DialogueLine> lines)
        {
            if (lines == null)
                return;
            foreach (DialogueLine line in lines)
                _queue.Enqueue(line);
            if (!_hasLine)
                StartNext();
        }

        /// <summary>The advance input: finish typing the line, or if it is typed, go to the next.</summary>
        public void Advance()
        {
            if (!_hasLine)
                return;
            int length = Length(_current);
            if (_typed < length)
            {
                _typed = length;
                Report();
                return;
            }
            StartNext();
        }

        /// <summary>Drops every line and clears the subtitle (a skipped cutscene).</summary>
        public void Clear()
        {
            bool wasBusy = IsBusy;
            _queue.Clear();
            _hasLine = false;
            _shown = -1;
            if (wasBusy)
                DialogueEvents.RaiseLineCleared();
        }

        /// <summary>Types and holds the current line by <paramref name="deltaTime"/> seconds.</summary>
        public void Tick(float deltaTime)
        {
            if (!_hasLine)
                return;

            int length = Length(_current);
            if (_typed < length)
            {
                int before = (int)_typed;
                _typed = Math.Min(length, _typed + deltaTime * CharactersPerSecond);
                for (int i = before; i < (int)_typed; i++)
                    if (!char.IsWhiteSpace(_current.text[i]))
                        Typed?.Invoke(_current.speaker);
                Report();
                return;
            }

            _held += deltaTime;
            if (_held >= HoldSeconds(length))
                StartNext();
        }

        void StartNext()
        {
            while (_queue.Count > 0)
            {
                DialogueLine next = _queue.Dequeue();
                if (!_conditionHolds(next.condition) || string.IsNullOrEmpty(next.text))
                    continue;
                _current = next;
                _hasLine = true;
                _typed = 0f;
                _held = 0f;
                _shown = -1;
                Report();
                return;
            }

            bool hadLine = _hasLine;
            _hasLine = false;
            if (hadLine)
                DialogueEvents.RaiseLineCleared();
        }

        // Tells the subtitles whenever the number of typed characters changes.
        void Report()
        {
            int visible = (int)_typed;
            if (visible == _shown)
                return;
            _shown = visible;
            DialogueEvents.RaiseLineShown(new DialogueLineView(
                DialogueSpeakers.Tag(_current.speaker), DialogueSpeakers.Colour(_current.speaker), _current.text, visible));
        }

        static int Length(in DialogueLine line) => line.text?.Length ?? 0;
    }
}

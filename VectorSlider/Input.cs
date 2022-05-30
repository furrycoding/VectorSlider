using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Forms;

namespace VectorSlider
{
    internal class InputManager
    {

        private Dictionary<string, Keys> keyBindings =
            new Dictionary<string, Keys>();

        private Dictionary<Keys, List<string>> reverseKeyBindings =
            new Dictionary<Keys, List<string>>();

        private Dictionary<string, MouseButtons> buttonBindings =
            new Dictionary<string, MouseButtons>();

        private Dictionary<MouseButtons, List<string>> reverseButtonBindings =
            new Dictionary<MouseButtons, List<string>>();

        private Dictionary<string, List<Action>> pressHandlers = new Dictionary<string, List<Action>>();
        private Dictionary<string, List<Action>> releaseHandlers = new Dictionary<string, List<Action>>();
        private Dictionary<string, List<Action<bool>>> doubleHandlers = new Dictionary<string, List<Action<bool>>>();


        public void AddKeyBinding(string name, Keys defaultKey)
        {
            if (!keyBindings.ContainsKey(name))
                keyBindings.Add(name, defaultKey);

            UpdateReverseBindings();
        }

        public void AddMouseButtonBinding(string name, MouseButtons defaultButton)
        {
            if (!buttonBindings.ContainsKey(name))
                buttonBindings.Add(name, defaultButton);

            UpdateReverseBindings();
        }

        public void AddHanlder(string inputName, Action pressHandler=null, Action releaseHandler=null, Action<bool> doubleHandler=null)
        {
            if (doubleHandler != null)
            {
                pressHandler = () => doubleHandler(true);
                releaseHandler = () => doubleHandler(false);
            }

            if (pressHandler != null)
            {
                if (!pressHandlers.ContainsKey(inputName))
                    pressHandlers.Add(inputName, new List<Action>());

                pressHandlers[inputName].Add(pressHandler);
            }

            if (releaseHandler != null)
            {
                if (!releaseHandlers.ContainsKey(inputName))
                    releaseHandlers.Add(inputName, new List<Action>());

                releaseHandlers[inputName].Add(releaseHandler);
            }
        }

        public void AttachToControl(Control control)
        {
            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.MouseDown += OnMouseButtonDown;
            control.MouseUp += OnMouseButtonUp;
        }

        public void DetachFromControl(Control control)
        {
            control.KeyDown -= OnKeyDown;
            control.KeyUp -= OnKeyUp;
            control.MouseDown -= OnMouseButtonDown;
            control.MouseUp -= OnMouseButtonUp;
        }

        private void UpdateReverseBindings()
        {
            reverseKeyBindings.Clear();
            foreach (var entry in keyBindings)
            {
                var key = entry.Value;
                if (!reverseKeyBindings.ContainsKey(key))
                    reverseKeyBindings.Add(key, new List<string>());

                reverseKeyBindings[key].Add(entry.Key);
            }

            reverseButtonBindings.Clear();
            foreach (var entry in buttonBindings)
            {
                var key = entry.Value;
                if (!reverseButtonBindings.ContainsKey(key))
                    reverseButtonBindings.Add(key, new List<string>());

                reverseButtonBindings[key].Add(entry.Key);
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs args)
        {
            HandleKey(args.KeyCode, pressHandlers);
        }

        private void OnKeyUp(object sender, KeyEventArgs args)
        {
            HandleKey(args.KeyCode, releaseHandlers);
        }

        private void OnMouseButtonDown(object sender, MouseEventArgs args)
        {
            HandleMouseButton(args.Button, pressHandlers);
        }

        private void OnMouseButtonUp(object sender, MouseEventArgs args)
        {
            HandleMouseButton(args.Button, releaseHandlers);
        }

        private void HandleKey(Keys keyId, Dictionary<string, List<Action>> handlerTable)
        {
            List<string> inputNames;
            if (!reverseKeyBindings.TryGetValue(keyId, out inputNames))
                return;

            InvokeHandlers(inputNames, handlerTable);
        }

        private void HandleMouseButton(MouseButtons buttonId, Dictionary<string, List<Action>> handlerTable)
        {
            List<string> inputNames;
            if (!reverseButtonBindings.TryGetValue(buttonId, out inputNames))
                return;

            InvokeHandlers(inputNames, handlerTable);
        }

        private void InvokeHandlers(List<string> inputNames, Dictionary<string, List<Action>> handlerTable)
        {
            List<Action> handlers;
            foreach (var name in inputNames)
                if (handlerTable.TryGetValue(name, out handlers))
                    foreach (var handler in handlers)
                        handler();
        }
    }
}

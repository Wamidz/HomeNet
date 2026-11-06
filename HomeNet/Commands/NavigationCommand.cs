using System;
using System.Windows.Input;

namespace HomeNet.Commands
{
    public class NavigationCommand : ICommand
    {
        private readonly string _url;
        private readonly Action<string> _navigateAction;

        public NavigationCommand(string url, Action<string> navigateAction)
        {
            _url = url;
            _navigateAction = navigateAction;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            _navigateAction?.Invoke(_url);
        }
    }
}
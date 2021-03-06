﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace WinFormsMVVM.MVVM
{
    public class BindingManager<TViewModel> : IDisposable
        where TViewModel : ViewModelBase, new()
    {
        private IList<ICommandBinder> Binders { get; }
        private IList<Command> Commands { get; } = new List<Command>();

        private Dictionary<string, PropertyChangedHandler<TViewModel>> PropertyBindings { get; }
            = new Dictionary<string, PropertyChangedHandler<TViewModel>>();

        internal TViewModel ViewModel { get; set; }

        public BindingManager()
        {
            ViewModel = new TViewModel();
            ViewModel.PropertyChanged += ViewModelOnPropertyChanged;

            Binders = new List<ICommandBinder>
            {
                new ControlBinder()
            };

            Application.Idle += OnIdle;
        }

        private void OnIdle(object sender, EventArgs e)
        {
            // ReSharper disable once UnusedVariable
            foreach (var command in Commands)
            {
                //command.Enabled = ...
            }
        }

        protected ICommandBinder FindBinder(IComponent component)
        {
            ICommandBinder binder = null;
            var type = component.GetType();
            while (type != null)
            {
                binder = Binders.FirstOrDefault(x => x.SourceType == type);
                if (binder != null)
                    return binder;

                type = type.BaseType;
            }

            if (binder == null)
                throw new Exception($"No binding found for component of type {component.GetType().Name}");

            return binder;
        }


        #region command binding

        public BindingManager<TViewModel> Bind(Command command, IComponent component)
        {
            if (!Commands.Contains(command))
                Commands.Add(command);

            FindBinder(component).Bind(command, component);
            return this;
        }

        public BindingManager<TViewModel> Bind(Expression<Func<TViewModel, Command>> commandExpr,
            IComponent targetComponent)
        {
            if (!(commandExpr.Body is MemberExpression member))
                throw new ArgumentException($"Expression '{commandExpr.Name}' refers to a method, not a property.");

            var fieldInfo = member.Member as FieldInfo;
            if (fieldInfo == null)
                throw new ArgumentException($"Expression '{commandExpr.Name}' does not refer to a field.");

            if (fieldInfo.ReflectedType == null ||
                typeof(TViewModel) != fieldInfo.ReflectedType &&
                !typeof(TViewModel).IsSubclassOf(fieldInfo.ReflectedType))
                throw new ArgumentException(
                    $"Expresion '{commandExpr}' refers to a property that is not from type {typeof(TViewModel).Name}.");

            var sourceGetter = commandExpr.Compile();

            var command = sourceGetter.Invoke(ViewModel);
            if (command != null) Bind(command, targetComponent);

            return this;
        }

        #endregion

        #region property binding

        public BindingManager<TViewModel> Bind<T>(Expression<Func<TViewModel, T>> sourceExpr, Action<T> setterDelegate)
        {
            if (!(sourceExpr.Body is MemberExpression member))
                throw new ArgumentException($"Expression '{sourceExpr.Name}' refers to a method, not a property.");

            var propInfo = member.Member as PropertyInfo;
            if (propInfo == null)
                throw new ArgumentException($"Expression '{sourceExpr.Name}' refers to a field, not a property.");

            if (propInfo.ReflectedType == null ||
                typeof(TViewModel) != propInfo.ReflectedType &&
                !typeof(TViewModel).IsSubclassOf(propInfo.ReflectedType))
                throw new ArgumentException(
                    $"Expresion '{sourceExpr}' refers to a property that is not from type {typeof(TViewModel).Name}.");

            var sourceGetter = sourceExpr.Compile();

            PropertyBindings.Add(propInfo.Name, new PropertyChangedHandler<TViewModel>(SynchronizationContext.Current,
                vm =>
                {
                    var val = sourceGetter.Invoke(vm);
                    setterDelegate.Invoke(val);
                }));

            return this;
        }

        #endregion


        private void ViewModelOnPropertyChanged(object sender, PropertyChangedEventArgs eventArgs)
        {
            if (string.IsNullOrWhiteSpace(eventArgs.PropertyName)) return;
            if (!PropertyBindings.ContainsKey(eventArgs.PropertyName)) return;

            var handler = PropertyBindings[eventArgs.PropertyName];
            handler.Execute((TViewModel) sender);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                Application.Idle -= OnIdle;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
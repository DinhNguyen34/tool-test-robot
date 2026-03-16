using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Core.Helpers
{
    public static class PrismHelper
    {
        private static IContainerProvider? _container;
        public static void Init(IContainerProvider containerProvider)
        {
            _container = containerProvider;
        }
        public static T? GetService<T>() where T : class 
        {
            return _container?.Resolve<T>();
        }
        public static T? GetService<T>(string name) where T : class
        {
            return _container?.Resolve<T>(name);
        }
    }
}

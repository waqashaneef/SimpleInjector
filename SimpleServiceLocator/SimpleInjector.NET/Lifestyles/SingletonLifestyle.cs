﻿#region Copyright (c) 2013 S. van Deursen
/* The Simple Injector is an easy-to-use Inversion of Control library for .NET
 * 
 * Copyright (C) 2013 S. van Deursen
 * 
 * To contact me, please visit my blog at http://www.cuttingedge.it/blogs/steven/ or mail to steven at 
 * cuttingedge.it.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and 
 * associated documentation files (the "Software"), to deal in the Software without restriction, including 
 * without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell 
 * copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the 
 * following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all copies or substantial 
 * portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT 
 * LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO 
 * EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER 
 * IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE 
 * USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion

namespace SimpleInjector.Lifestyles
{
    using System;
    using System.Linq.Expressions;
    using System.Threading;
    using SimpleInjector.Advanced;
    using SimpleInjector.Extensions.Decorators;

    internal sealed class SingletonLifestyle : Lifestyle
    {
        internal SingletonLifestyle() : base("Singleton")
        {
        }

        protected override int Length
        {
            get { return 1000; }
        }

        // This method seems somewhat redundant, since the exact same can be achieved by calling
        // Lifetime.Singleton.CreateRegistration(serviceType, () => instance, container). Calling that method
        // however, will trigger the ExpressionBuilding event later on where the supplied Expression is an
        // InvocationExpression calling that internally created Func<TService>. By having this extra method
        // (and the extra SingletonInstanceLifestyleRegistration class), we can ensure that the
        // ExpressionBuilding event is called with a ConstantExpression, which is much more intuitive to
        // anyone handling that event.
        internal static Registration CreateSingleRegistration(Type serviceType, object instance, 
            Container container, Type implementationType = null)
        {
            Requires.IsNotNull(instance, "instance");

            // When running in a sandbox, getting the type of the instance might work when the type is
            // internal. We might to walk the hierarchy up from the implementation type to the service type
            // and get the first public base type (or interface) we encounter, or perhaps simply fall back to
            // the service type directly. Problem with this is that we probably don't want to let the fx
            // behave differently inside a sandbox, since that would confuse users.
            // TODO: Find out if this is a problem and how to go around this.
            implementationType = implementationType ?? instance.GetType();

            return new SingletonInstanceLifestyleRegistration(serviceType, implementationType, instance, 
                Lifestyle.Singleton, container);
        }

        protected override Registration CreateRegistrationCore<TService, TImplementation>(
            Container container)
        {
            return new SingletonLifestyleRegistration<TService, TImplementation>(this, container);
        }

        protected override Registration CreateRegistrationCore<TService>(Func<TService> instanceCreator,
            Container container)
        {
            return new SingletonFuncLifestyleRegistration<TService>(instanceCreator, this, container);
        }

        private sealed class SingletonInstanceLifestyleRegistration : Registration
        {
            private readonly object originalInstance;
            private readonly Type serviceType;
            private readonly Type implementationType;
            private readonly Lazy<object> initializedInstance;

            internal SingletonInstanceLifestyleRegistration(Type serviceType, Type implementationType, 
                object instance, Lifestyle lifestyle, Container container)
                : base(lifestyle, container)
            {
                this.originalInstance = instance;
                this.serviceType = serviceType;
                this.implementationType = implementationType;

                // Default lazy behavior ensures that the initializer is guaranteed to be called just once.
                this.initializedInstance = new Lazy<object>(this.GetInjectedInterceptedAndInitializedInstance);
            }

            public override Type ImplementationType
            {
                get { return this.implementationType; }
            }

            public override Expression BuildExpression()
            {
                return Expression.Constant(this.initializedInstance.Value, this.serviceType);
            }

            private object GetInjectedInterceptedAndInitializedInstance()
            {
                try
                {
                    return this.GetInjectedInterceptedAndInitializedInstanceInternal();
                }
                catch (MemberAccessException ex)
                {
                    throw new ActivationException(
                        StringResources.UnableToResolveTypeDueToSecurityConfiguration(this.serviceType, ex));
                }
            }

            private object GetInjectedInterceptedAndInitializedInstanceInternal()
            {
                Expression expression = Expression.Constant(this.originalInstance, this.serviceType);

                expression = this.WrapWithPropertyInjector(this.serviceType, this.implementationType, expression);

                expression = this.InterceptInstanceCreation(this.serviceType, this.implementationType, expression);

                expression = this.WrapWithInitializer(this.serviceType, this.implementationType, expression);

                var initializer = Expression.Lambda(expression).Compile();

                // This delegate might return a different instance than the originalInstance.
                return initializer.DynamicInvoke();
            }
        }
        
        private sealed class SingletonFuncLifestyleRegistration<TService> 
            : SingletonLifestyleRegistrationBase<TService>
            where TService : class
        {
            private Func<TService> instanceCreator;

            internal SingletonFuncLifestyleRegistration(Func<TService> instanceCreator, Lifestyle lifestyle,
                Container container)
                : base(lifestyle, container)
            {
                this.instanceCreator = instanceCreator;
            }

            public override Type ImplementationType
            {
                get { return typeof(TService); }
            }

            protected override TService CreateInstance()
            {
                return this.BuildTransientDelegate<TService>(this.instanceCreator)();
            }
        }

        private class SingletonLifestyleRegistration<TService, TImplementation>
            : SingletonLifestyleRegistrationBase<TService>
            where TImplementation : class, TService
            where TService : class
        {
            public SingletonLifestyleRegistration(Lifestyle lifestyle, Container container)
                : base(lifestyle, container)
            {
            }

            public override Type ImplementationType
            {
                get { return typeof(TImplementation); }
            }

            protected override TService CreateInstance()
            {
                return this.BuildTransientDelegate<TService, TImplementation>()();
            }
        }

        private abstract class SingletonLifestyleRegistrationBase<TService> : Registration 
            where TService : class
        {
            private readonly Lazy<TService> lazyInstance;

            protected SingletonLifestyleRegistrationBase(Lifestyle lifestyle, Container container)
                : base(lifestyle, container)
            {
                // Even though the InstanceProducer takes a lock before calling Registration.BuildExpression
                // we want to be very sure that there will never be more than one instance of a singleton
                // created. Since the same Registration instance can be used by multiple InstanceProducers,
                // we absolutely need this protection.
                this.lazyInstance = new Lazy<TService>(this.CreateInstanceWithNullCheck);
            }

            public override Expression BuildExpression()
            {
                return Expression.Constant(this.lazyInstance.Value, typeof(TService));
            }

            protected abstract TService CreateInstance();

            private TService CreateInstanceWithNullCheck()
            {
                var instance = this.CreateInstance();

                EnsureInstanceIsNotNull(instance);

                return instance;
            }

            private static void EnsureInstanceIsNotNull(object instance)
            {
                if (instance == null)
                {
                    throw new ActivationException(
                        StringResources.DelegateForTypeReturnedNull(typeof(TService)));
                }
            }
        }
    }
}
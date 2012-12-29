﻿namespace SimpleInjector.Tests.Unit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class VerifyTests
    {
        [TestMethod]
        public void Verify_WithEmptyConfiguration_Succeeds()
        {
            // Arrange
            var container = new Container();

            // Act
            container.Verify();
        }

        [TestMethod]
        public void Verify_Never_LocksContainer1()
        {
            // Arrange
            var container = new Container();

            container.Verify();

            // Act
            container.RegisterSingle<IUserRepository>(new SqlUserRepository());
        }

        [TestMethod]
        public void Verify_Never_LocksContainer2()
        {
            // Arrange
            var container = new Container();

            container.Register<ITimeProvider>(() =>
            {
                // Sneaky call back into the container.
                return container.GetInstance<RealTimeProvider>();
            });

            container.Verify();

            // Act
            container.RegisterSingle<IUserRepository>(new SqlUserRepository());
        }

        [TestMethod]
        public void Verify_CalledMultipleTimes_Succeeds()
        {
            // Arrange
            var container = new Container();

            container.RegisterSingle<IUserRepository>(new SqlUserRepository());

            container.Verify();

            container.Register<UserServiceBase>(() => container.GetInstance<RealUserService>());

            // Act
            container.Verify();
        }

        [TestMethod]
        public void Verify_CalledAfterGetInstance_DoesNotUnlockTheContainer()
        {
            // Arrange
            var container = new Container();

            container.RegisterSingle<IUserRepository>(new SqlUserRepository());

            container.GetInstance<IUserRepository>();

            container.Verify();

            try
            {
                // Act
                container.Register<ITimeProvider, RealTimeProvider>();

                Assert.Fail("The container was expected to stay locked.");
            }
            catch (InvalidOperationException ex)
            {
                Assert.IsTrue(ex.Message.Contains("Container can't be changed"), "Actual: " + ex.Message);
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException), "Registration of a type after validation should fail, because the container should be locked down.")]
        public void Verify_WithEmptyConfiguration_ThrowsException()
        {
            // Arrange
            var container = new Container();
            container.RegisterSingle<RealUserService>();
            container.Verify();

            // Act
            container.Register<UserServiceBase>(() => container.GetInstance<RealUserService>());
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException), "An exception was expected because the configuration is invalid without registering an IUserRepository.")]
        public void Verify_WithDependantTypeNotRegistered_ThrowsException()
        {
            // Arrange
            var container = new Container();

            // RealUserService has a constructor that takes an IUserRepository.
            container.RegisterSingle<RealUserService>();

            // Act
            container.Verify();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Verify_WithFailingFunc_ThrowsException()
        {
            // Arrange
            var container = new Container();
            container.Register<IUserRepository>(() =>
            {
                throw new ArgumentNullException();
            });

            // Act
            container.Verify();
        }

        [TestMethod]
        public void Verify_RegisteredCollectionWithValidElements_Succeeds()
        {
            // Arrange
            var container = new Container();
            container.RegisterAll<IUserRepository>(new IUserRepository[] { new SqlUserRepository(), new InMemoryUserRepository() });

            // Act
            container.Verify();
        }

        [TestMethod]
        public void Verify_RegisteredCollectionWithNullElements_ThrowsException()
        {
            // Arrange
            var container = new Container();

            IEnumerable<IUserRepository> repositories = new IUserRepository[] { null };

            container.RegisterAll<IUserRepository>(repositories);

            try
            {
                // Act
                container.Verify();

                // Assert
                Assert.Fail("Exception expected.");
            }
            catch (InvalidOperationException ex)
            {
                AssertThat.StringContains(
                    "One of the items in the collection for type IUserRepository is a null reference.",
                    ex.Message);
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Verify_FailingCollection_ThrowsException()
        {
            // Arrange
            var container = new Container();

            IEnumerable<IUserRepository> repositories =
                from nullRepository in Enumerable.Repeat<IUserRepository>(null, 1)
                where nullRepository.ToString() == "This line fails with an NullReferenceException"
                select nullRepository;

            container.RegisterAll<IUserRepository>(repositories);

            // Act
            container.Verify();
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Verify_RegisterCalledWithFuncReturningNullInstances_ThrowsExpectedException()
        {
            // Arrange
            var container = new Container();

            container.Register<IUserRepository>(() => null);

            // Act
            container.Verify();
        }
    }
}
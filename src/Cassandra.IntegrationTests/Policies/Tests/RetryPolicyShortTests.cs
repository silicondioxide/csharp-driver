//
//      Copyright (C) 2017 DataStax Inc.
//
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using System;
using System.Threading;
using Cassandra.IntegrationTests.TestBase;
using Cassandra.IntegrationTests.TestClusterManagement;
using NUnit.Framework;

namespace Cassandra.IntegrationTests.Policies.Tests
{
    [TestFixture, Category("short")]
    public class RetryPolicyShortTests : TestGlobals
    {
        private SCassandraManager _scassandraManager;

        [OneTimeTearDown]
        public void OnTearDown()
        {
            TestClusterManager.TryRemove();
            if (_scassandraManager != null)
            {
                _scassandraManager.Stop();
            }
        }
        
        [TestCase("overloaded", typeof(OverloadedException))]
        [TestCase("is_bootstrapping", typeof(IsBootstrappingException))]
        public void RetryPolicy_Extended(string resultError, Type exceptionType)
        {
            _scassandraManager = SCassandraManager.Instance;
            var extendedRetryPolicy = new TestExtendedRetryPolicy();
            var builder = Cluster.Builder()
                                 .AddContactPoint("127.0.0.1")
                                 .WithPort(_scassandraManager.BinaryPort)
                                 .WithRetryPolicy(extendedRetryPolicy)
                                 .WithReconnectionPolicy(new ConstantReconnectionPolicy(long.MaxValue));
            using (var cluster = builder.Build())
            {
                var session = (Session) cluster.Connect();
                const string cql = "select * from table1";
                _scassandraManager.PrimeQuery(cql, "{\"result\" : \"" + resultError + "\"}").Wait();
                Exception throwedException = null;
                try
                {
                    session.Execute(cql);
                }
                catch (Exception ex)
                {
                    throwedException = ex;
                }
                finally
                {
                    Assert.NotNull(throwedException);
                    Assert.AreEqual(throwedException.GetType(), exceptionType);
                    Assert.AreEqual(1, Interlocked.Read(ref extendedRetryPolicy.RequestErrorConter));
                    Assert.AreEqual(0, Interlocked.Read(ref extendedRetryPolicy.ReadTimeoutCounter));
                    Assert.AreEqual(0, Interlocked.Read(ref extendedRetryPolicy.WriteTimeoutCounter));
                    Assert.AreEqual(0, Interlocked.Read(ref extendedRetryPolicy.UnavailableCounter));
                }
            }
        }

        class TestExtendedRetryPolicy : IExtendedRetryPolicy
        {
            public long ReadTimeoutCounter;
            public long WriteTimeoutCounter;
            public long UnavailableCounter;
            public long RequestErrorConter;

            public RetryDecision OnReadTimeout(IStatement query, ConsistencyLevel cl, int requiredResponses, int receivedResponses, bool dataRetrieved,
                                               int nbRetry)
            {
                Interlocked.Increment(ref ReadTimeoutCounter);
                return RetryDecision.Rethrow();
            }

            public RetryDecision OnWriteTimeout(IStatement query, ConsistencyLevel cl, string writeType, int requiredAcks, int receivedAcks,
                                                int nbRetry)
            {
                Interlocked.Increment(ref WriteTimeoutCounter);
                return RetryDecision.Rethrow();
            }

            public RetryDecision OnUnavailable(IStatement query, ConsistencyLevel cl, int requiredReplica, int aliveReplica, int nbRetry)
            {
                Interlocked.Increment(ref UnavailableCounter);
                return RetryDecision.Rethrow();
            }

            public RetryDecision OnRequestError(IStatement statement, Configuration config, Exception ex, int nbRetry)
            {
                Interlocked.Increment(ref RequestErrorConter);
                return RetryDecision.Rethrow();
            }
        }
    }
}

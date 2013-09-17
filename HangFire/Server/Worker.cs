﻿using System;
using System.Collections.Generic;

using HangFire.Storage;

using ServiceStack.Logging;

namespace HangFire.Server
{
    internal class Worker
    {
        public static readonly RedisStorage Redis = new RedisStorage();

        protected readonly ILog Logger;
        private readonly ServerContext _serverContext;
        private readonly int _workerNumber;
        private readonly ServerJobInvoker _jobInvoker;
        private readonly JobActivator _jobActivator;

        public Worker(
            ServerContext serverContext,
            int workerNumber,
            ServerJobInvoker jobInvoker, JobActivator jobActivator)
        {
            Logger = LogManager.GetLogger(String.Format("HangFire.Worker.{0}", workerNumber));
            _serverContext = serverContext;
            _workerNumber = workerNumber;
            _jobInvoker = jobInvoker;
            _jobActivator = jobActivator;
        }

        public virtual void Process(string jobId)
        {
            string jobType = null; 
            Dictionary<string, string> jobArgs = null;

            lock (Redis)
            {
                Redis.RetryOnRedisException(x => 
                    x.GetJobTypeAndArgs(jobId, out jobType, out jobArgs));
            }

            if (String.IsNullOrEmpty(jobType))
            {
                Logger.Warn(String.Format(
                    "Could not process the job '{0}': it does not exist in the storage.",
                    jobId));

                return;
            }

            var workerContext = new WorkerContext(_serverContext, _workerNumber);

            lock (Redis)
            {
                Redis.RetryOnRedisException(x => 
                    x.AddProcessingWorker(workerContext.ServerContext.ServerName, jobId));
            }

            Exception exception = null;

            ServerJobDescriptor jobDescriptor = null;
            try
            {
                jobDescriptor = new ServerJobDescriptor(_jobActivator, jobId, jobType, jobArgs);
                _jobInvoker.PerformJob(workerContext, jobDescriptor);
            }
            catch (Exception ex)
            {
                exception = ex;

                Logger.Error(String.Format(
                    "Failed to process the job '{0}': unexpected exception caught.",
                    jobId));
            }
            finally
            {
                if (jobDescriptor != null)
                {
                    jobDescriptor.Dispose();
                }
            }

            lock (Redis)
            {
                Redis.RetryOnRedisException(x => 
                    x.RemoveProcessingWorker(jobId, exception));
            }
        }
    }
}
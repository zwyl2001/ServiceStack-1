﻿using System;
using System.Net;
using System.Runtime.Serialization;
using NUnit.Framework;
using Funq;
using ServiceStack.Text;
using ServiceStack.Web;

namespace ServiceStack.WebHost.Endpoints.Tests
{
    [Route("/users")]
    public class User { }
    public class UserResponse : IHasResponseStatus
    {
        public ResponseStatus ResponseStatus { get; set; }
    }

    public class UserService : Service
    {
        public object Get(User request)
        {
            return new HttpError(HttpStatusCode.BadRequest, "CanNotExecute", "Failed to execute!");
        }

        public object Post(User request)
        {
            throw new HttpError(HttpStatusCode.BadRequest, "CanNotExecute", "Failed to execute!");
        }

        public object Delete(User request)
        {
            throw new HttpError(HttpStatusCode.Forbidden, "CanNotExecute", "Failed to execute!");
        }

        public object Put(User request)
        {
            throw new ArgumentException();
        }
    }

    public class CustomException : ArgumentException
    {
        public CustomException() : base("User Defined Error") { }
    }

    public class ExceptionWithResponseStatus { }
    public class ExceptionWithResponseStatusResponse
    {
        public ResponseStatus ResponseStatus { get; set; }
    }
    public class ExceptionWithResponseStatusService : Service
    {
        public object Any(ExceptionWithResponseStatus request)
        {
            throw new CustomException();
        }
    }

    public class ExceptionNoResponseStatus { }
    public class ExceptionNoResponseStatusResponse { }
    public class ExceptionNoResponseStatusService : Service
    {
        public object Any(ExceptionNoResponseStatus request)
        {
            throw new CustomException();
        }
    }

    public class ExceptionNoResponseDto { }
    public class ExceptionNoResponseDtoService : Service
    {
        public object Any(ExceptionNoResponseDto request)
        {
            throw new CustomException();
        }
    }

    public class UncatchedException { }
    public class UncatchedExceptionResponse { }
    public class UncatchedExceptionService : Service
    {
        public object Any(UncatchedException request)
        {
            //We don't wrap a try..catch block around the service (which happens with ServiceBase<> automatically)
            //so the global exception handling strategy is invoked
            throw new ArgumentException();
        }
    }

    [Route("/binding-error/{Id}")]
    public class ExceptionWithRequestBinding
    {
        public int Id { get; set; }
    }

    public class ExceptionWithRequestBindingService : Service
    {
        public object Any(ExceptionWithRequestBinding request)
        {
            return request;
        }
    }


    [TestFixture]
    public class ExceptionHandlingTests
    {
        private const string ListeningOn = "http://localhost:82/";

        public class ExceptionHandlingAppHostHttpListener
            : AppHostHttpListenerBase
        {

            public ExceptionHandlingAppHostHttpListener()
                : base("Exception handling tests", typeof(UserService).Assembly) { }

            public override void Configure(Container container)
            {
                JsConfig.EmitCamelCaseNames = true;

                SetConfig(new HostConfig { DebugMode = false });

                //Custom global uncaught exception handling strategy
                this.UncaughtExceptionHandlers.Add((req, res, operationName, ex) =>
                {
                    res.Write(string.Format("UncaughtException {0}", ex.GetType().Name));
                    res.EndRequest(skipHeaders: true);
                });

                this.ServiceExceptionHandlers.Add((httpReq, request, exception) =>
                {
                    if (request is UncatchedException)
                        throw exception;

                    return null;
                });
            }

            public override void OnExceptionTypeFilter(Exception ex, ResponseStatus responseStatus)
            {
                "In OnExceptionTypeFilter...".Print();
                base.OnExceptionTypeFilter(ex, responseStatus);
            }

            public override void OnUncaughtException(IRequest httpReq, IResponse httpRes, string operationName, Exception ex)
            {
                "In OnUncaughtException...".Print();
                base.OnUncaughtException(httpReq, httpRes, operationName, ex);
            }
        }

        ExceptionHandlingAppHostHttpListener appHost;

        [TestFixtureSetUp]
        public void OnTestFixtureSetUp()
        {
            appHost = new ExceptionHandlingAppHostHttpListener();
            appHost.Init();
            appHost.Start(ListeningOn);
        }

        [TestFixtureTearDown]
        public void OnTestFixtureTearDown()
        {
            appHost.Dispose();
            appHost.UncaughtExceptionHandlers = null;
        }

        static IRestClient[] ServiceClients = 
		{
			new JsonServiceClient(ListeningOn),
			new XmlServiceClient(ListeningOn),
			new JsvServiceClient(ListeningOn)
			//SOAP not supported in HttpListener
			//new Soap11ServiceClient(ServiceClientBaseUri),
			//new Soap12ServiceClient(ServiceClientBaseUri)
		};


        [Test, TestCaseSource("ServiceClients")]
        public void Handles_Returned_Http_Error(IRestClient client)
        {
            try
            {
                client.Get<UserResponse>("/users");
                Assert.Fail();
            }
            catch (WebServiceException ex)
            {
                Assert.That(ex.ErrorCode, Is.EqualTo("CanNotExecute"));
                Assert.That(ex.StatusCode, Is.EqualTo((int)System.Net.HttpStatusCode.BadRequest));
                Assert.That(ex.Message, Is.EqualTo("CanNotExecute"));
            }
        }

        [Test, TestCaseSource("ServiceClients")]
        public void Handles_Thrown_Http_Error(IRestClient client)
        {
            try
            {
                client.Post<UserResponse>("/users", new User());
                Assert.Fail();
            }
            catch (WebServiceException ex)
            {
                Assert.That(ex.ErrorCode, Is.EqualTo("CanNotExecute"));
                Assert.That(ex.StatusCode, Is.EqualTo((int)System.Net.HttpStatusCode.BadRequest));
                Assert.That(ex.Message, Is.EqualTo("CanNotExecute"));
            }
        }

        [Test, TestCaseSource("ServiceClients")]
        public void Handles_Thrown_Http_Error_With_Forbidden_status_code(IRestClient client)
        {
            try
            {
                client.Delete<UserResponse>("/users");
                Assert.Fail();
            }
            catch (WebServiceException ex)
            {
                Assert.That(ex.ErrorCode, Is.EqualTo("CanNotExecute"));
                Assert.That(ex.StatusCode, Is.EqualTo((int)System.Net.HttpStatusCode.Forbidden));
                Assert.That(ex.Message, Is.EqualTo("CanNotExecute"));
            }
        }

        [Test, TestCaseSource("ServiceClients")]
        public void Handles_Normal_Exception(IRestClient client)
        {
            try
            {
                client.Put<UserResponse>("/users", new User());
                Assert.Fail();
            }
            catch (WebServiceException ex)
            {
                Assert.That(ex.ErrorCode, Is.EqualTo("ArgumentException"));
                Assert.That(ex.StatusCode, Is.EqualTo((int)System.Net.HttpStatusCode.BadRequest));
            }
        }

        public string PredefinedJsonUrl<T>()
        {
            return ListeningOn + "json/reply/" + typeof(T).Name;
        }

        [Test]
        public void Returns_populated_dto_when_has_ResponseStatus()
        {
            try
            {
                var json = PredefinedJsonUrl<ExceptionWithResponseStatus>().GetJsonFromUrl();
                Assert.Fail("Should throw");
            }
            catch (WebException webEx)
            {
                var errorResponse = ((HttpWebResponse)webEx.Response);
                var body = errorResponse.GetResponseStream().ReadFully().FromUtf8Bytes();
                Assert.That(body, Is.EqualTo(
                    "{\"responseStatus\":{\"errorCode\":\"CustomException\",\"message\":\"User Defined Error\",\"errors\":[]}}"));
            }
        }

        [Test]
        public void Returns_empty_dto_when_NoResponseStatus()
        {
            try
            {
                var json = PredefinedJsonUrl<ExceptionNoResponseStatus>().GetJsonFromUrl();
                Assert.Fail("Should throw");
            }
            catch (WebException webEx)
            {
                var errorResponse = ((HttpWebResponse)webEx.Response);
                var body = errorResponse.GetResponseStream().ReadFully().FromUtf8Bytes();
                Assert.That(body, Is.EqualTo("{}"));
            }
        }

        [Test]
        public void Returns_no_body_when_NoResponseDto()
        {
            try
            {
                var json = PredefinedJsonUrl<ExceptionNoResponseDto>().GetJsonFromUrl();
                Assert.Fail("Should throw");
            }
            catch (WebException webEx)
            {
                var errorResponse = ((HttpWebResponse)webEx.Response);
                var body = errorResponse.GetResponseStream().ReadFully().FromUtf8Bytes();
                Assert.That(body, Is.StringStarting("{\"responseStatus\":{\"errorCode\":\"CustomException\",\"message\":\"User Defined Error\""));
            }
        }

        [Test]
        public void Can_override_global_exception_handling()
        {
            var req = (HttpWebRequest)WebRequest.Create(PredefinedJsonUrl<UncatchedException>());
            var res = req.GetResponse().ReadToEnd();
            Assert.AreEqual("UncaughtException ArgumentException", res);
        }

        [Test]
        public void Request_binding_error_raises_UncaughtException()
        {
            var response = PredefinedJsonUrl<ExceptionWithRequestBinding>()
                .AddQueryParam("Id", "NaN")
                .GetStringFromUrl();

            Assert.That(response, Is.EqualTo("UncaughtException SerializationException"));
        }
    }
}

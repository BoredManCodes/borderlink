#nullable enable
using BorderLink.Server.Services;
using Bitbound.SimpleMessenger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using BorderLink.Server.Hubs;
using BorderLink.Server.Services.Stores;
using BorderLink.Server.Tests.Mocks;
using BorderLink.Shared;
using BorderLink.Shared.Entities;
using BorderLink.Shared.Enums;
using BorderLink.Shared.Extensions;
using BorderLink.Shared.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BorderLink.Server.Tests;

[TestClass]
public class CircuitConnectionTests
{
#nullable disable
    private TestData _testData;
    private IDataService _dataService;
    private Mock<IAuthService> _authService;
    private Mock<ISelectedCardsStore> _clientAppState;
    private HubContextFixture<AgentHub, IAgentHubClient> _agentHubContextFixture;
    private Mock<ICircuitManager> _circuitManager;
    private Mock<IToastService> _toastService;
    private Mock<IExpiringTokenService> _expiringTokenService;
    private Mock<IRemoteControlSessionCache> _remoteControlSessionCache;
    private Mock<IMessenger> _messenger;
    private Mock<IAgentHubSessionCache> _agentSessionCache;
    private Mock<IInventoryService> _inventoryService;
    private Mock<IServicesService> _servicesService;
    private Mock<IProcessesService> _processesService;
    private Mock<IPatchService> _patchService;
    private Mock<IAuditLogService> _auditLog;
    private Mock<ILogger<CircuitConnection>> _logger;
    private CircuitConnection _circuitConnection;
#nullable enable

    [TestInitialize]
    public async Task Init()
    {
        _testData = new TestData();
        await _testData.Init();

        _dataService = IoCActivator.ServiceProvider.GetRequiredService<IDataService>();
        _authService = new Mock<IAuthService>();
        _clientAppState = new Mock<ISelectedCardsStore>();
        _agentHubContextFixture = new HubContextFixture<AgentHub, IAgentHubClient>();
        _circuitManager = new Mock<ICircuitManager>();
        _toastService = new Mock<IToastService>();
        _expiringTokenService = new Mock<IExpiringTokenService>();
        _remoteControlSessionCache = new Mock<IRemoteControlSessionCache>();
        _messenger = new Mock<IMessenger>();
        _agentSessionCache = new Mock<IAgentHubSessionCache>();
        _inventoryService = new Mock<IInventoryService>();
        _servicesService = new Mock<IServicesService>();
        _processesService = new Mock<IProcessesService>();
        _patchService = new Mock<IPatchService>();
        _auditLog = new Mock<IAuditLogService>();
        _logger = new Mock<ILogger<CircuitConnection>>();

        _circuitConnection = new CircuitConnection(
            _authService.Object,
            _dataService,
            _inventoryService.Object,
            _servicesService.Object,
            _processesService.Object,
            _patchService.Object,
            _clientAppState.Object,
            _agentHubContextFixture.HubContextMock.Object,
            _circuitManager.Object,
            _toastService.Object,
            _expiringTokenService.Object,
            _remoteControlSessionCache.Object,
            _agentSessionCache.Object,
            _messenger.Object,
            _auditLog.Object,
            _logger.Object);
    }

    [TestMethod]
    public async Task ControlDeviceService_GivenUserIsUnauthorized_Fails()
    {
        _circuitConnection.User = _testData.Org1User1;

        // Device exists in another org so the test user has no access.
        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        var success = await _circuitConnection.ControlDeviceService(
            _testData.Org2Device1.ID,
            "Spooler",
            "start");

        Assert.IsFalse(success);
        _servicesService.Verify(
            x => x.ControlServiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task ControlDeviceService_GivenInvalidAction_Fails()
    {
        _circuitConnection.User = _testData.Org1User1;

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);
        Assert.IsTrue(addToGroupResult);

        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        var success = await _circuitConnection.ControlDeviceService(
            _testData.Org1Device1.ID,
            "Spooler",
            "delete");

        Assert.IsFalse(success);
        _servicesService.Verify(
            x => x.ControlServiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    [DataRow("start")]
    [DataRow("stop")]
    [DataRow("restart")]
    public async Task ControlDeviceService_GivenAuthorizedAndValidAction_DelegatesToService(string action)
    {
        _circuitConnection.User = _testData.Org1User1;

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);
        Assert.IsTrue(addToGroupResult);

        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        _servicesService
            .Setup(x => x.ControlServiceAsync(_testData.Org1Device1.ID, "Spooler", action, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(true);

        var success = await _circuitConnection.ControlDeviceService(
            _testData.Org1Device1.ID,
            "Spooler",
            action);

        Assert.IsTrue(success);
        _servicesService.Verify(
            x => x.ControlServiceAsync(_testData.Org1Device1.ID, "Spooler", action, It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task KillDeviceProcess_GivenUserIsUnauthorized_Fails()
    {
        _circuitConnection.User = _testData.Org1User1;

        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        var success = await _circuitConnection.KillDeviceProcess(_testData.Org2Device1.ID, 1234);

        Assert.IsFalse(success);
        _processesService.Verify(
            x => x.KillProcessAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<System.Threading.CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task KillDeviceProcess_GivenAuthorized_DelegatesToService()
    {
        _circuitConnection.User = _testData.Org1User1;

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);
        Assert.IsTrue(addToGroupResult);

        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        _processesService
            .Setup(x => x.KillProcessAsync(_testData.Org1Device1.ID, 4242, It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(true);

        var success = await _circuitConnection.KillDeviceProcess(_testData.Org1Device1.ID, 4242);

        Assert.IsTrue(success);
        _processesService.Verify(
            x => x.KillProcessAsync(_testData.Org1Device1.ID, 4242, It.IsAny<System.Threading.CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task WakeDevice_GivenUserIsUnauthorized_Fails()
    {
        // A standard user won't have access if they aren't in the same
        // group as the device.
        _circuitConnection.User = _testData.Org1User1;

        // Offline device.
        _testData.Org1Device1.PublicIP = "142.251.33.110";
        _testData.Org1Device1.MacAddresses = new[] { "78E3B5A1E45B" };
        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        // Online device.
        _testData.Org1Device2.PublicIP = "142.251.33.110";
        // Device in another org that shouldn't receive the command.
        _testData.Org2Device1.PublicIP = "142.251.33.110";


        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device2.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device2.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org2Device1.ID, _testData.Org2Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        var wakeResult = await _circuitConnection.WakeDevice(_testData.Org1Device1);
        Assert.IsFalse(wakeResult.IsSuccess);

        _agentSessionCache.VerifyNoOtherCalls();
        _agentHubContextFixture.HubContextMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task WakeDevice_GivenMatchingPeerByIp_UsesCorrectPeer()
    {
        _circuitConnection.User = _testData.Org1User1;

        var macAddress = "78E3B5A1E45B";

        // Offline device.
        _testData.Org1Device1.PublicIP = "142.251.33.110";
        _testData.Org1Device1.MacAddresses = new[] { macAddress };
        // Online device.
        _testData.Org1Device2.PublicIP = "142.251.33.110";
        // Device in another org that shouldn't receive the command.
        _testData.Org2Device1.PublicIP = "142.251.33.110";

        // Offline device in the same group as user.
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);

        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device2.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        _agentSessionCache
            .Setup(x => x.GetAllDevices())
            .Returns(new[]
            {
                _testData.Org1Device2,
                _testData.Org2Device1
            });

        var connectionId = "HQUSIBxiOwNokVH_mYgGyg";

        _agentSessionCache
            .Setup(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId))
            .Returns(true);

        var wakeResult = await _circuitConnection.WakeDevice(_testData.Org1Device1);

        Assert.IsTrue(addToGroupResult);
        Assert.IsTrue(wakeResult.IsSuccess);


        _agentSessionCache
            .Verify(x => x.GetAllDevices(), Times.Once);

        _agentSessionCache
            .Verify(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId), Times.Once);

        _agentHubContextFixture.HubClientsMock
            .Verify(x => x.Client(connectionId), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock
            .Verify(x =>
                x.WakeDevice(macAddress), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock.VerifyNoOtherCalls();
        _agentHubContextFixture.HubContextMock.VerifyNoOtherCalls();
        _agentSessionCache.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task WakeDevice_GivenMatchingPeerByGroupId_UsesCorrectPeer()
    {
        _circuitConnection.User = _testData.Org1User1;

        var macAddress = "78E3B5A1E45B";

        // Offline device.
        _testData.Org1Device1.PublicIP = "142.251.33.110";
        _testData.Org1Device1.MacAddresses = new[] { macAddress };

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);

        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        // Offline device.
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);
        // Online device in the same group and org.  Should relay wake command.
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device2.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);
        // Online device in a different org.  Should not receive wake command.
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org2Device1.ID, _testData.Org2Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);


        _agentSessionCache
            .Setup(x => x.GetAllDevices())
            .Returns(new[]
            {
                _testData.Org1Device2,
                _testData.Org2Device1
            });

        var connectionId = "HQUSIBxiOwNokVH_mYgGyg";

        _agentSessionCache
            .Setup(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId))
            .Returns(true);

        var wakeResult = await _circuitConnection.WakeDevice(_testData.Org1Device1);

        Assert.IsTrue(addToGroupResult);
        Assert.IsTrue(wakeResult.IsSuccess);


        _agentSessionCache
            .Verify(x => x.GetAllDevices(), Times.Once);

        _agentSessionCache
            .Verify(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId), Times.Once);

        _agentHubContextFixture.HubClientsMock
            .Verify(x => x.Client(connectionId), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock
            .Verify(x =>
                x.WakeDevice(macAddress), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock.VerifyNoOtherCalls();
        _agentHubContextFixture.HubContextMock.VerifyNoOtherCalls();
        _agentSessionCache.VerifyNoOtherCalls();
    }


    [TestMethod]
    public async Task WakeDevice_GivenNoMatchingGroupOrIp_DoesNotSend()
    {
        _circuitConnection.User = _testData.Org1User1;

        var macAddress = "78E3B5A1E45B";

        // Offline device.
        _testData.Org1Device1.PublicIP = "142.251.33.110";
        _testData.Org1Device1.MacAddresses = new[] { macAddress };
        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        // Online device, but in a different group.
        _testData.Org1Device2.DeviceGroupID = _testData.Org1Group2.ID;
        // Device in another org that shouldn't receive the command.
        _testData.Org2Device1.DeviceGroupID = _testData.Org2Group1.ID;

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);

        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device2.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);


        // Offline device.
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        // Online device in a different group.  Should not recieve wake command.
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device2.ID, _testData.Org1Group2.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        // Online device in a different org.  Should not recieve wake command.
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org2Device1.ID, _testData.Org2Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        _agentSessionCache
            .Setup(x => x.GetAllDevices())
            .Returns(new[]
            {
                _testData.Org1Device2,
                _testData.Org2Device1
            });

        var wakeResult = await _circuitConnection.WakeDevice(_testData.Org1Device1);

        Assert.IsTrue(addToGroupResult);
        Assert.IsTrue(wakeResult.IsSuccess);


        _agentSessionCache
            .Verify(x => x.GetAllDevices(), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock.VerifyNoOtherCalls();
        _agentHubContextFixture.HubContextMock.VerifyNoOtherCalls();
        _agentSessionCache.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task WakeDevices_GivenPeerIpMatches_UsesCorrectPeer()
    {
        _circuitConnection.User = _testData.Org1User1;

        var macAddress = "78E3B5A1E45B";

        // Offline device.
        _testData.Org1Device1.PublicIP = "142.251.33.110";
        _testData.Org1Device1.MacAddresses = new[] { macAddress };
        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        // Online device.
        _testData.Org1Device2.PublicIP = "142.251.33.110";
        // Device in another org that shouldn't receive the command.
        _testData.Org2Device1.PublicIP = "142.251.33.110";

        // Offline device in the same group as user.
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);

        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device2.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        _agentSessionCache
            .Setup(x => x.GetAllDevices())
            .Returns(new[]
            {
                _testData.Org1Device2,
                _testData.Org2Device1
            });

        var connectionId = "HQUSIBxiOwNokVH_mYgGyg";

        _agentSessionCache
            .Setup(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId))
            .Returns(true);

        var wakeResult = await _circuitConnection.WakeDevices(new[] { _testData.Org1Device1 });

        Assert.IsTrue(addToGroupResult);
        Assert.IsTrue(wakeResult.IsSuccess);


        _agentSessionCache
            .Verify(x => x.GetAllDevices(), Times.Once);

        _agentSessionCache
            .Verify(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId), Times.Once);

        _agentHubContextFixture.HubClientsMock
            .Verify(x => x.Client(connectionId), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock
            .Verify(x =>
                x.WakeDevice(macAddress), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock.VerifyNoOtherCalls();
        _agentHubContextFixture.HubContextMock.VerifyNoOtherCalls();
        _agentSessionCache.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task WakeDevices_GivenMatchingPeerByGroupId_UsesCorrectPeer()
    {
        _circuitConnection.User = _testData.Org1User1;

        var macAddress = "78E3B5A1E45B";

        // Offline device.
        _testData.Org1Device1.PublicIP = "142.251.33.110";
        _testData.Org1Device1.MacAddresses = new[] { macAddress };
        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        // Online device.
        _testData.Org1Device2.DeviceGroupID = _testData.Org1Group1.ID;
        // Device in another org that shouldn't receive the command.
        _testData.Org2Device1.DeviceGroupID = _testData.Org2Group1.ID;

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);

        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device2.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        // Offline device.
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);
        // Online device in the same group and org.  Should relay wake command.
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device2.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);
        // Online device in a different org.  Should not receive wake command.
        addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org2Device1.ID, _testData.Org2Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        _agentSessionCache
            .Setup(x => x.GetAllDevices())
            .Returns(new[]
            {
                _testData.Org1Device2,
                _testData.Org2Device1
            });

        var connectionId = "HQUSIBxiOwNokVH_mYgGyg";

        _agentSessionCache
            .Setup(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId))
            .Returns(true);

        var wakeResult = await _circuitConnection.WakeDevices(new[] { _testData.Org1Device1 });

        Assert.IsTrue(addToGroupResult);
        Assert.IsTrue(wakeResult.IsSuccess);


        _agentSessionCache
            .Verify(x => x.GetAllDevices(), Times.Once);

        _agentSessionCache
            .Verify(x => x.TryGetConnectionId(_testData.Org1Device2.ID, out connectionId), Times.Once);

        _agentHubContextFixture.HubClientsMock
            .Verify(x => x.Client(connectionId), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock
            .Verify(x =>
                x.WakeDevice(macAddress), Times.Once);

        _agentHubContextFixture.SingleClientProxyMock.VerifyNoOtherCalls();
        _agentHubContextFixture.HubContextMock.VerifyNoOtherCalls();
        _agentSessionCache.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task RequestPatchInstall_GivenUserIsUnauthorized_ReturnsNull()
    {
        _circuitConnection.User = _testData.Org1User1;

        // Device is in another org so the test user has no access.
        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        var run = await _circuitConnection.RequestPatchInstall(
            _testData.Org2Device1.ID,
            "abcd-1234",
            "Test Update");

        Assert.IsNull(run);
        _patchService.Verify(
            x => x.RequestInstallAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task RequestPatchInstall_GivenAuthorized_DelegatesToService()
    {
        _circuitConnection.User = _testData.Org1User1;

        var addToGroupResult = _dataService.AddUserToDeviceGroup(
            _testData.Org1Id,
            _testData.Org1Group1.ID,
            $"{_testData.Org1User1.UserName}",
            out _);
        Assert.IsTrue(addToGroupResult);

        _testData.Org1Device1.DeviceGroupID = _testData.Org1Group1.ID;
        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org1Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);
        var addGroupResult = await _dataService.AddDeviceToGroup(_testData.Org1Device1.ID, _testData.Org1Group1.ID);
        Assert.IsTrue(addGroupResult.IsSuccess);

        var expected = new PatchInstallRun
        {
            Id = Guid.NewGuid(),
            DeviceID = _testData.Org1Device1.ID,
            OrganizationID = _testData.Org1Id,
            UpdateId = "abcd-1234",
            UpdateTitle = "Test Update",
            Status = PatchInstallStatus.Pending,
        };

        _patchService
            .Setup(x => x.RequestInstallAsync(
                _testData.Org1Device1.ID,
                "abcd-1234",
                "Test Update",
                _testData.Org1User1.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var run = await _circuitConnection.RequestPatchInstall(
            _testData.Org1Device1.ID,
            "abcd-1234",
            "Test Update");

        Assert.IsNotNull(run);
        Assert.AreEqual(expected.Id, run!.Id);
        _patchService.Verify(
            x => x.RequestInstallAsync(
                _testData.Org1Device1.ID,
                "abcd-1234",
                "Test Update",
                _testData.Org1User1.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _auditLog.Verify(
            x => x.LogAsync(
                AuditActions.PatchInstallRequested,
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task GetDevicePendingUpdates_GivenUserIsUnauthorized_ReturnsEmpty()
    {
        _circuitConnection.User = _testData.Org1User1;

        var updateResult = await _dataService.AddOrUpdateDevice(_testData.Org2Device1.ToDto());
        Assert.IsTrue(updateResult.IsSuccess);

        var updates = await _circuitConnection.GetDevicePendingUpdates(_testData.Org2Device1.ID);

        Assert.IsNotNull(updates);
        Assert.AreEqual(0, updates.Length);
        _patchService.Verify(
            x => x.GetPendingUpdatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

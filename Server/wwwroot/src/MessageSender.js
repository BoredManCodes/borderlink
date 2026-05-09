import { ViewerApp } from "./App.js";
import { CtrlAltDelDto, KeyDownDto, KeyPressDto, KeyUpDto, MouseDownDto, MouseMoveDto, MouseUpDto, MouseWheelDto, SelectScreenDto, TapDto, ToggleAudioDto, ToggleBlockInputDto, TextTransferDto, FileDto, WindowsSessionsDto, EmptyDto, FrameReceivedDto } from "./Interfaces/Dtos.js";
import { CreateGUID } from "./Utilities.js";
import { FileTransferProgress } from "./UI.js";
import { DtoType } from "./Enums/DtoType.js";
import { RemoteControlMode } from "./Enums/RemoteControlMode.js";
export class MessageSender {
    async GetWindowsSessions() {
        if (ViewerApp.Mode == RemoteControlMode.Unattended) {
            var dto = new WindowsSessionsDto();
            await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.WindowsSessions);
        }
    }
    async ChangeWindowsSession(sessionId) {
        await ViewerApp.ViewerHubConnection.ChangeWindowsSession(sessionId);
    }
    async SendFrameReceived(timestamp) {
        var dto = new FrameReceivedDto(timestamp);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.FrameReceived);
    }
    async SendSelectScreen(displayName) {
        var dto = new SelectScreenDto(displayName);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.SelectScreen);
    }
    async SendMouseMove(percentX, percentY) {
        var dto = new MouseMoveDto(percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseMove);
    }
    async SendMouseDown(button, percentX, percentY) {
        var dto = new MouseDownDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseDown);
    }
    async SendMouseUp(button, percentX, percentY) {
        var dto = new MouseUpDto(button, percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseUp);
    }
    async SendTap(percentX, percentY) {
        var dto = new TapDto(percentX, percentY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.Tap);
    }
    async SendMouseWheel(deltaX, deltaY) {
        var dto = new MouseWheelDto(deltaX, deltaY);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.MouseWheel);
    }
    async SendKeyDown(key) {
        var dto = new KeyDownDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyDown);
    }
    async SendKeyUp(key) {
        var dto = new KeyUpDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyUp);
    }
    async SendKeyPress(key) {
        var dto = new KeyPressDto(key);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.KeyPress);
    }
    async SendSetKeyStatesUp() {
        var dto = new EmptyDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.SetKeyStatesUp);
    }
    async SendCtrlAltDel() {
        var dto = new CtrlAltDelDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.CtrlAltDel);
        await ViewerApp.ViewerHubConnection.InvokeCtrlAltDel();
    }
    /**
     * Sends a keyboard shortcut to the remote machine. Each key in the array is
     * pressed down in order, then released in reverse order — matching how
     * physical chords work (e.g. ["Control", "Shift", "Escape"] for Task Manager).
     */
    async SendKeyCombo(keys) {
        for (const key of keys) {
            await this.SendKeyDown(key);
        }
        for (let i = keys.length - 1; i >= 0; i--) {
            await this.SendKeyUp(keys[i]);
        }
    }
    /**
     * Launches a Windows app/command on the remote by opening the Run dialog
     * (Win+R), typing the command, and pressing Enter. Works in attended mode
     * with a logged-in desktop session.
     */
    async LaunchViaRun(command) {
        await this.SendKeyCombo(["Meta", "r"]);
        // Give the Run dialog a moment to open and grab focus.
        await new Promise((r) => setTimeout(r, 350));
        await this.SendTextTransfer(command, true);
        // Tiny pause so the typed text is fully delivered before Enter.
        await new Promise((r) => setTimeout(r, 80));
        await this.SendKeyCombo(["Enter"]);
    }
    async SendOpenFileTransferWindow() {
        var dto = new EmptyDto();
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.OpenFileTransferWindow);
    }
    async SendFile(buffer, fileName) {
        var messageId = CreateGUID();
        let dto = new FileDto(null, fileName, messageId, false, true);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);
        for (var i = 0; i < buffer.byteLength; i += 50000) {
            let dto = new FileDto(buffer.slice(i, i + 50000), fileName, messageId, false, false);
            await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);
            if (i > 0) {
                FileTransferProgress.value = i / buffer.byteLength;
            }
        }
        dto = new FileDto(null, fileName, messageId, true, false);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.File);
    }
    async SendToggleAudio(toggleOn) {
        var dto = new ToggleAudioDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleAudio);
    }
    ;
    async SendToggleBlockInput(toggleOn) {
        var dto = new ToggleBlockInputDto(toggleOn);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.ToggleBlockInput);
    }
    async SendTextTransfer(text, typeText) {
        var dto = new TextTransferDto(text, typeText);
        await ViewerApp.ViewerHubConnection.SendDtoToClient(dto, DtoType.TextTransfer);
    }
}
//# sourceMappingURL=MessageSender.js.map
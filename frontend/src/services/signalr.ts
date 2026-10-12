import * as signalR from '@microsoft/signalr'
import { API_BASE_URL, api } from './api'
import type {
  CliOutputEvent,
  ExecutionCompletedEvent,
  ExecutionProgressEvent,
  ExecutionStartedEvent,
  ExecutionStateChangedEvent,
  SchedulerUpdatedEvent,
} from '@/types/models'

export type SignalRConnectionState = 'connected' | 'connecting' | 'reconnecting' | 'disconnected'

class SignalRService {
  private connection: signalR.HubConnection | null = null
  private stateCallbacks = new Set<(state: SignalRConnectionState) => void>()
  private startedCallbacks = new Set<(ev: ExecutionStartedEvent) => void>()
  private outputCallbacks = new Set<(ev: CliOutputEvent) => void>()
  private progressCallbacks = new Set<(ev: ExecutionProgressEvent) => void>()
  private stateChangedCallbacks = new Set<(ev: ExecutionStateChangedEvent) => void>()
  private completedCallbacks = new Set<(ev: ExecutionCompletedEvent) => void>()
  private schedulerCallbacks = new Set<(ev: SchedulerUpdatedEvent) => void>()

  public currentState: SignalRConnectionState = 'disconnected'

  private setState(state: SignalRConnectionState) {
    this.currentState = state
    this.stateCallbacks.forEach((cb) => cb(state))
  }

  public async connect(): Promise<void> {
    if (
      this.connection &&
      (this.connection.state === signalR.HubConnectionState.Connected ||
        this.connection.state === signalR.HubConnectionState.Connecting)
    ) {
      return
    }

    this.setState('connecting')

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/cli`)
      .withAutomaticReconnect([0, 1500, 3000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    this.connection.onreconnecting(() => {
      this.setState('reconnecting')
    })

    this.connection.onreconnected(() => {
      this.setState('connected')
    })

    this.connection.onclose(() => {
      this.setState('disconnected')
    })

    this.connection.on('ExecutionStarted', (payload: ExecutionStartedEvent) => {
      this.startedCallbacks.forEach((cb) => cb(payload))
    })

    this.connection.on('CliOutput', (payload: CliOutputEvent) => {
      this.outputCallbacks.forEach((cb) => cb(payload))
    })

    this.connection.on('ExecutionProgress', (payload: ExecutionProgressEvent) => {
      this.progressCallbacks.forEach((cb) => cb(payload))
    })

    this.connection.on('ExecutionStateChanged', (payload: ExecutionStateChangedEvent) => {
      this.stateChangedCallbacks.forEach((cb) => cb(payload))
    })

    this.connection.on('ExecutionCompleted', (payload: ExecutionCompletedEvent) => {
      this.completedCallbacks.forEach((cb) => cb(payload))
    })

    this.connection.on('SchedulerUpdated', (payload: SchedulerUpdatedEvent) => {
      this.schedulerCallbacks.forEach((cb) => cb(payload))
    })

    try {
      await this.connection.start()
      this.setState('connected')
    } catch {
      this.setState('disconnected')
      setTimeout(() => {
        if (this.currentState === 'disconnected') {
          this.connect().catch(() => {})
        }
      }, 4000)
    }
  }

  public async cancelExecution(executionId: number): Promise<void> {
    try {
      if (this.connection && this.connection.state === signalR.HubConnectionState.Connected) {
        await this.connection.invoke('CancelExecution', executionId).catch(async () => {
          await api.cancelExecution(executionId)
        })
      } else {
        await api.cancelExecution(executionId)
      }
    } catch {
      await api.cancelExecution(executionId)
    }
  }

  public onStateChange(cb: (state: SignalRConnectionState) => void) {
    this.stateCallbacks.add(cb)
    cb(this.currentState)
    return () => this.stateCallbacks.delete(cb)
  }

  public onExecutionStarted(cb: (ev: ExecutionStartedEvent) => void) {
    this.startedCallbacks.add(cb)
    return () => this.startedCallbacks.delete(cb)
  }

  public onCliOutput(cb: (ev: CliOutputEvent) => void) {
    this.outputCallbacks.add(cb)
    return () => this.outputCallbacks.delete(cb)
  }

  public onExecutionProgress(cb: (ev: ExecutionProgressEvent) => void) {
    this.progressCallbacks.add(cb)
    return () => this.progressCallbacks.delete(cb)
  }

  public onExecutionStateChanged(cb: (ev: ExecutionStateChangedEvent) => void) {
    this.stateChangedCallbacks.add(cb)
    return () => this.stateChangedCallbacks.delete(cb)
  }

  public onExecutionCompleted(cb: (ev: ExecutionCompletedEvent) => void) {
    this.completedCallbacks.add(cb)
    return () => this.completedCallbacks.delete(cb)
  }

  public onSchedulerUpdated(cb: (ev: SchedulerUpdatedEvent) => void) {
    this.schedulerCallbacks.add(cb)
    return () => this.schedulerCallbacks.delete(cb)
  }
}

export const signalrService = new SignalRService()

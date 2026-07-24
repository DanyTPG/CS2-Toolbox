# CS2 Assistant Flowchart

```mermaid
graph TB
    Main[Main Thread - Hotkey Poll<br/>50ms Cycle] -->|F9| Calibrate[Print Calibration<br/>+ RGB/HSV + Beep]
    Main -->|F10| Toggle[Toggle Active Status<br/>+ Beep on Toggle]
    Main -->|F11| Stop[Stop Assistant<br/>+ Exit]
    
    subgraph "Threads"
        Direction LR
        AutoAccept[Auto-Accept Thread<br/>Scan every 1s]
        AntiAfk[Anti-AFK Thread<br/>Random 30-90s refresh]
        AutoQueue[Auto-Queue Thread<br/>LOBBY → QUEUING]
    end
    
    Main --> AutoAccept
    Main --> AntiAfk
    Main --> AutoQueue
    
    AutoAccept -->|Active & Enabled?| Q1{Scan<br/>500+ Green Pixels?}
    Q1 -->|Yes| Click1[Click Accept Pixels<br/>+ Log + Wait 5s]
    Q1 -->|No| Sleep1[Sleep 1s<br/>scan cycle]
    Click1 --> Sleep1
    
    AntiAfk -->|Active & Enabled?| Q2{CS2 Active &<br/>Cursor Hidden?}
    Q2 -->|Yes| WASD[Random WASD +<br/>Opposing Key + Mouse Jitter]
    Q2 -->|No| SleepAnti[Sleep 30-90s]
    WASD --> SleepAnti
    
    AutoQueue -->|Active & Enabled?| Q3{CS2 Active &<br/>Cursor Visible<br/>+ > 10s?}
    Q3 -->|Yes| Q4{Queue State<br/>= LOBBY?}
    Q3 -->|No| ResetQueue[Reset Queue<br/>State]
    
    Q4 -->|Yes| Lobbies[LOBBY State]
    Lobbies -->|Green<br/>Queue Indicator?| IsQueuing{Queue Indicator<br/>Green?}
    IsQ:Queue State = QUEUING?
    IsQ -->|Yes| Lobbies
    Lobbies -->|Go Button<br/>Green?| Go1{Go Button<br/>Green?}
    
    Go1 -->|Yes| ClickGo[Click Go Button<br/>State = QUEUING<br/>Reset Timers]
    Go1 -->|No| Navigate[Navigate Play →<br/>Matchmaking → Premier<br/>Every 15s]
    
    IsQueuing --- Q4
    IsQueuing -->|No| LobbyWait{Are Timers<br/>> 2s?}
    LobbyWait -->|Yes| ScanQue[Scan Queue<br/>Interval]
    
    Navigate -->|After 2s| SleepQueue[Sleep 1s]
    ClickGo --> SleepQueue
    
    ScanQue -->|Still<br/>Searching?| WaitQueue[Wait 10s Scan]
    WaitQueue --> SleepQueue
    SleepQueue --> Lobbies
    SleepAnti --> AntiAfk
    Sleep1 --> AutoAccept
    
    ResetQueue --> AutoQueue
    
    style Main fill:#cbd5e1,stroke:#475569,stroke-width:2px
    style AutoAccept fill:#a5d8ff,stroke:#475569,stroke-width:2px
    style AntiAfk fill:#a5d8ff,stroke:#475569,stroke-width:2px
    style AutoQueue fill:#a5d8ff,stroke:#475569,stroke-width:2px
```

## Flow Summary

| Thread | Interval | Key Logic |
|--------|----------|-----------|
| Main | 50ms | Hotkey polling (F9/F10/F11) |
| Auto-Accept | 1s | Scan 30% screen region for 500+ green pixels |
| Anti-AFK | 30-90s random | WASD + opposing key + mouse jitter (only when cursor hidden) |
| Auto-Queue | 1s | State machine: LOBBY (navigate Play→Matchmaking→Premier) or discover queue |

**Important State Reset Triggers:**
- Cursor hidden or CS2 inactive → reset cursor duration + queue state
- In-game detected → queue state reset to LOBBY
- Go button detected in QUEUING → queue cancelled

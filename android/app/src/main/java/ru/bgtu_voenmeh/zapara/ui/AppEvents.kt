package ru.bgtu_voenmeh.zapara.ui

import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

sealed interface AppEvent {
    data object ScheduleChanged : AppEvent
    data object PersonalizationChanged : AppEvent
    data object GroupChanged : AppEvent
}

class AppEvents {
    private val bus = MutableSharedFlow<AppEvent>(extraBufferCapacity = 64)
    val events: SharedFlow<AppEvent> = bus.asSharedFlow()
    fun emit(event: AppEvent) { bus.tryEmit(event) }
}

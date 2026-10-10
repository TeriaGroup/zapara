@extends('errors.layout')
@section('code', '500')
@section('title', 'Что-то пошло не так')
@section('message', 'На сервере произошла ошибка. Подробности записаны в журнал сервера. Попробуйте ещё раз чуть позже.')
@section('actions')
    <a class="zp-primary" href="{{ url('/admin') }}">На инфопанель</a>
    <a href="{{ \App\Support\ErrorPages::backUrl() }}">Обновить страницу</a>
@endsection

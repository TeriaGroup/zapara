@extends('errors.layout')
{{-- #149: общий запасной вариант для 5xx без своей страницы. --}}
@section('code', isset($exception) && method_exists($exception, 'getStatusCode') ? (string) $exception->getStatusCode() : '5xx')
@section('title', 'Что-то пошло не так')
@section('message', 'На сервере произошла ошибка. Подробности записаны в журнал сервера. Попробуйте ещё раз чуть позже.')
@section('actions')
    <a class="zp-primary" href="{{ url('/admin') }}">На инфопанель</a>
    <a href="{{ url()->previous(url('/admin')) }}">Обновить страницу</a>
@endsection

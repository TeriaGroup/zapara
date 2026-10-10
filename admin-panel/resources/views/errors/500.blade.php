@extends('errors.layout')
@section('code', '500')
@section('title', 'Что-то пошло не так')
@section('message', 'На сервере произошла ошибка. Мы уже записали её в журнал. Попробуйте ещё раз чуть позже.')
@section('actions')
    <a class="zp-primary" href="{{ url('/admin') }}">На инфопанель</a>
    <a href="{{ url()->current() }}">Обновить страницу</a>
@endsection

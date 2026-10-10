@extends('errors.layout')
@section('code', '503')
@section('title', 'Панель временно недоступна')
@section('message', 'Идут технические работы. Обычно это занимает несколько минут.')
@section('actions')
    <a class="zp-primary" href="{{ url()->current() }}">Обновить страницу</a>
@endsection

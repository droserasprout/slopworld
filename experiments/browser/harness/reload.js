(() => {
  if (window.__benchmark) throw new Error('Refusing reload while a benchmark owns sessions');
  setTimeout(() => location.reload(), 100);
  return {reloading:true};
})()
